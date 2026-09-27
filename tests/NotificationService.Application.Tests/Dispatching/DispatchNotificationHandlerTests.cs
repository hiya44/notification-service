using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NotificationService.Application.Dispatching;
using NotificationService.Application.Providers;
using NotificationService.Application.Tests.Fakes;
using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Tests.Dispatching;

public class DispatchNotificationHandlerTests
{
    private readonly FakeTimeProvider _time = new(TestNotifications.Now);
    private readonly FakeProvider _provider = new("Twilio", Channel.Sms);
    private readonly InMemoryNotificationRepository _repository = new();
    private readonly DispatchNotificationHandler _handler;

    public DispatchNotificationHandlerTests()
    {
        var providerOptions = new TestOptionsMonitor<NotificationOptions>(new NotificationOptions
        {
            Providers = { ["Twilio"] = new ProviderOptions { Priority = 1, Channels = [Channel.Sms] } },
        });
        var dispatcher = new NotificationDispatcher(
            new ProviderSelector([_provider], providerOptions),
            new TestOptionsMonitor<DispatchOptions>(new DispatchOptions()),
            _time,
            NullLogger<NotificationDispatcher>.Instance);

        _handler = new DispatchNotificationHandler(_repository, dispatcher, _time, NullLogger<DispatchNotificationHandler>.Instance);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HandleAsync_WithDueNotification_DispatchesAndSavesIt()
    {
        var notification = await AddAsync(TestNotifications.Sms());

        await _handler.HandleAsync(notification.Id, CancellationToken);

        notification.Status.ShouldBe(NotificationStatus.Delivered);
        _repository.UpdateCount.ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_WhenAllProvidersFail_SavesScheduledRetry()
    {
        _provider.WillFailTransiently();
        var notification = await AddAsync(TestNotifications.Sms());

        await _handler.HandleAsync(notification.Id, CancellationToken);

        notification.Status.ShouldBe(NotificationStatus.Pending);
        notification.NextAttemptAt.ShouldNotBeNull().ShouldBeGreaterThan(TestNotifications.Now);
        _repository.UpdateCount.ShouldBe(1);
    }

    [Fact]
    public async Task HandleAsync_WhenAlreadyDelivered_SkipsWithoutCallingProviders()
    {
        var notification = TestNotifications.Sms();
        notification.RecordDeliveryAttempt("Vonage", DeliveryOutcome.Delivered(), TestNotifications.Now);
        await AddAsync(notification);

        await _handler.HandleAsync(notification.Id, CancellationToken);

        _provider.Requests.ShouldBeEmpty();
        _repository.UpdateCount.ShouldBe(0);
    }

    [Fact]
    public async Task HandleAsync_WhenRetryNotYetDue_SkipsWithoutCallingProviders()
    {
        // E.g. our lease expired and another instance already dispatched it and scheduled a retry.
        var notification = await AddAsync(TestNotifications.Sms(createdAt: TestNotifications.Now.AddMinutes(1)));

        await _handler.HandleAsync(notification.Id, CancellationToken);

        _provider.Requests.ShouldBeEmpty();
        _repository.UpdateCount.ShouldBe(0);
    }

    [Fact]
    public async Task HandleAsync_WhenNotificationDoesNotExist_DoesNothing()
    {
        await _handler.HandleAsync(NotificationId.New(), CancellationToken);

        _provider.Requests.ShouldBeEmpty();
        _repository.UpdateCount.ShouldBe(0);
    }

    [Fact]
    public async Task HandleAsync_WhenSaveConflicts_DoesNotThrow()
    {
        var notification = await AddAsync(TestNotifications.Sms());
        _repository.ConflictOnNextUpdate = true;

        await Should.NotThrowAsync(() => _handler.HandleAsync(notification.Id, CancellationToken));

        _repository.UpdateCount.ShouldBe(0);
    }

    [Fact]
    public async Task HandleAsync_WhenCancelledDuringDispatch_ThrowsWithoutSaving()
    {
        _provider.WillHang();
        var notification = await AddAsync(TestNotifications.Sms());
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

        var handling = _handler.HandleAsync(notification.Id, shutdown.Token);
        await shutdown.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(handling);
        _repository.UpdateCount.ShouldBe(0);
    }

    private async Task<Notification> AddAsync(Notification notification)
    {
        (await _repository.TryAddAsync(notification, CancellationToken)).ShouldBeTrue();
        return notification;
    }
}
