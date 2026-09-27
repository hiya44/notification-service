using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NotificationService.Application.Dispatching;
using NotificationService.Application.Providers;
using NotificationService.Application.Tests.Fakes;
using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Tests.Dispatching;

public class NotificationDispatcherTests
{
    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMinutes(1);

    private readonly FakeTimeProvider _time = new(TestNotifications.Now);
    private readonly FakeProvider _primary = new("Primary", Channel.Sms);
    private readonly FakeProvider _secondary = new("Secondary", Channel.Sms);
    private readonly TestOptionsMonitor<NotificationOptions> _providerOptions;
    private readonly TestOptionsMonitor<DispatchOptions> _dispatchOptions;
    private readonly NotificationDispatcher _dispatcher;

    public NotificationDispatcherTests()
    {
        _providerOptions = new(ProviderOptionsWith(("Primary", 1, true), ("Secondary", 2, true)));
        _dispatchOptions = new(DispatchOptionsWith(maxDispatches: 3));
        _dispatcher = new NotificationDispatcher(
            new ProviderSelector([_primary, _secondary], _providerOptions),
            _dispatchOptions,
            _time,
            NullLogger<NotificationDispatcher>.Instance);
    }

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Dispatch_WhenPrimaryDelivers_DoesNotTryOtherProviders()
    {
        var notification = TestNotifications.Sms();

        await _dispatcher.DispatchAsync(notification, CancellationToken);

        notification.Status.ShouldBe(NotificationStatus.Delivered);
        notification.DeliveryAttempts.ShouldHaveSingleItem().ProviderName.ShouldBe("Primary");
        _secondary.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dispatch_WhenPrimaryFailsTransiently_FailsOverToSecondary()
    {
        _primary.WillFailTransiently("Service unavailable");
        var notification = TestNotifications.Sms();

        await _dispatcher.DispatchAsync(notification, CancellationToken);

        notification.Status.ShouldBe(NotificationStatus.Delivered);
        notification.DeliveryAttempts.Select(a => (a.ProviderName, a.Outcome)).ShouldBe(new[]
        {
            ("Primary", DeliveryOutcomeKind.TransientFailure),
            ("Secondary", DeliveryOutcomeKind.Delivered),
        });
        notification.DeliveryAttempts[0].FailureReason.ShouldBe("Service unavailable");
    }

    [Fact]
    public async Task Dispatch_WhenPrimaryThrows_TreatsItAsTransientAndFailsOver()
    {
        _primary.WillThrow(new HttpRequestException("Connection refused"));
        var notification = TestNotifications.Sms();

        await _dispatcher.DispatchAsync(notification, CancellationToken);

        notification.Status.ShouldBe(NotificationStatus.Delivered);
        var failedAttempt = notification.DeliveryAttempts[0];
        failedAttempt.Outcome.ShouldBe(DeliveryOutcomeKind.TransientFailure);
        failedAttempt.FailureReason.ShouldBe("Unexpected HttpRequestException from provider.");
        notification.DeliveryAttempts[1].ProviderName.ShouldBe("Secondary");
    }

    [Fact]
    public async Task Dispatch_WhenPrimaryDoesNotRespondInTime_FailsOverToSecondary()
    {
        _primary.WillHang();
        var notification = TestNotifications.Sms();

        var dispatch = _dispatcher.DispatchAsync(notification, CancellationToken);
        _time.Advance(AttemptTimeout);
        await dispatch;

        notification.Status.ShouldBe(NotificationStatus.Delivered);
        var timedOutAttempt = notification.DeliveryAttempts[0];
        timedOutAttempt.Outcome.ShouldBe(DeliveryOutcomeKind.TransientFailure);
        timedOutAttempt.FailureReason.ShouldNotBeNull();
        timedOutAttempt.FailureReason.ShouldStartWith("No response within");
        notification.DeliveryAttempts[1].ProviderName.ShouldBe("Secondary");
    }

    [Fact]
    public async Task Dispatch_WhenPrimaryReportsPermanentFailure_FailsWithoutTryingOthers()
    {
        _primary.WillReturn(DeliveryOutcome.PermanentFailure("Number does not exist"));
        var notification = TestNotifications.Sms();

        await _dispatcher.DispatchAsync(notification, CancellationToken);

        notification.Status.ShouldBe(NotificationStatus.Failed);
        notification.FailureReason.ShouldBe("Number does not exist");
        _secondary.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dispatch_WhenAllProvidersFail_SchedulesRetry()
    {
        _primary.WillFailTransiently();
        _secondary.WillFailTransiently();
        var notification = TestNotifications.Sms();

        await _dispatcher.DispatchAsync(notification, CancellationToken);

        notification.Status.ShouldBe(NotificationStatus.Pending);
        notification.DeliveryAttempts.Count.ShouldBe(2);
        notification.FailedDispatchCount.ShouldBe(1);
        notification.NextAttemptAt.ShouldBe(TestNotifications.Now + InitialRetryDelay);
    }

    [Fact]
    public async Task Dispatch_OnRetry_StartsAgainFromHighestPriorityProvider()
    {
        _primary.WillFailTransiently();
        _secondary.WillFailTransiently();
        var notification = TestNotifications.Sms();
        await _dispatcher.DispatchAsync(notification, CancellationToken);

        _time.Advance(InitialRetryDelay);
        await _dispatcher.DispatchAsync(notification, CancellationToken);

        notification.Status.ShouldBe(NotificationStatus.Delivered);
        notification.DeliveryAttempts.Select(a => a.ProviderName)
            .ShouldBe(new[] { "Primary", "Secondary", "Primary" });
    }

    [Fact]
    public async Task Dispatch_WhenRetriesAreExhausted_FailsNotification()
    {
        _dispatchOptions.CurrentValue = DispatchOptionsWith(maxDispatches: 2);
        _primary.WillFailTransiently().WillFailTransiently();
        _secondary.WillFailTransiently().WillFailTransiently();
        var notification = TestNotifications.Sms();

        await _dispatcher.DispatchAsync(notification, CancellationToken);
        _time.Advance(InitialRetryDelay);
        await _dispatcher.DispatchAsync(notification, CancellationToken);

        notification.Status.ShouldBe(NotificationStatus.Failed);
        notification.FailedDispatchCount.ShouldBe(2);
        notification.DeliveryAttempts.Count.ShouldBe(4);
        notification.NextAttemptAt.ShouldBeNull();
    }

    [Fact]
    public async Task Dispatch_WhenNoProviderIsEligible_SchedulesRetryWithoutAttempts()
    {
        _providerOptions.CurrentValue = ProviderOptionsWith(("Primary", 1, false), ("Secondary", 2, false));
        var notification = TestNotifications.Sms();

        await _dispatcher.DispatchAsync(notification, CancellationToken);

        notification.Status.ShouldBe(NotificationStatus.Pending);
        notification.DeliveryAttempts.ShouldBeEmpty();
        notification.FailedDispatchCount.ShouldBe(1);
        notification.NextAttemptAt.ShouldBe(TestNotifications.Now + InitialRetryDelay);
    }

    [Fact]
    public async Task Dispatch_WhenPrimaryIsDisabled_UsesSecondary()
    {
        _providerOptions.CurrentValue = ProviderOptionsWith(("Primary", 1, false), ("Secondary", 2, true));
        var notification = TestNotifications.Sms();

        await _dispatcher.DispatchAsync(notification, CancellationToken);

        notification.Status.ShouldBe(NotificationStatus.Delivered);
        notification.DeliveryAttempts.ShouldHaveSingleItem().ProviderName.ShouldBe("Secondary");
        _primary.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dispatch_WhenPrioritiesAreSwapped_UsesNewOrder()
    {
        _providerOptions.CurrentValue = ProviderOptionsWith(("Primary", 2, true), ("Secondary", 1, true));
        var notification = TestNotifications.Sms();

        await _dispatcher.DispatchAsync(notification, CancellationToken);

        notification.DeliveryAttempts.ShouldHaveSingleItem().ProviderName.ShouldBe("Secondary");
    }

    [Fact]
    public async Task Dispatch_SendsNotificationDetailsToProvider()
    {
        var notification = TestNotifications.Sms();

        await _dispatcher.DispatchAsync(notification, CancellationToken);

        var request = _primary.Requests.ShouldHaveSingleItem();
        request.NotificationId.ShouldBe(notification.Id);
        request.Recipient.ShouldBe(notification.Recipient);
        request.Content.ShouldBe(notification.Content);
    }

    [Fact]
    public async Task Dispatch_WhenCancelled_StopsWithoutSchedulingRetry()
    {
        _primary.WillHang();
        var notification = TestNotifications.Sms();
        using var shutdown = new CancellationTokenSource();

        var dispatch = _dispatcher.DispatchAsync(notification, shutdown.Token);
        await shutdown.CancelAsync();

        await Should.ThrowAsync<TaskCanceledException>(dispatch);
        notification.Status.ShouldBe(NotificationStatus.Pending);
        notification.FailedDispatchCount.ShouldBe(0);
        _secondary.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dispatch_WhenNotificationIsNotPending_ThrowsWithoutCallingProviders()
    {
        var notification = TestNotifications.Sms();
        await _dispatcher.DispatchAsync(notification, CancellationToken);

        await Should.ThrowAsync<InvalidOperationException>(
            () => _dispatcher.DispatchAsync(notification, CancellationToken));

        _primary.Requests.Count.ShouldBe(1);
    }

    private static NotificationOptions ProviderOptionsWith(params (string Name, int Priority, bool Enabled)[] providers)
    {
        var options = new NotificationOptions();
        foreach (var (name, priority, enabled) in providers)
        {
            options.Providers[name] = new ProviderOptions { Enabled = enabled, Priority = priority, Channels = [Channel.Sms] };
        }

        return options;
    }

    private static DispatchOptions DispatchOptionsWith(int maxDispatches) => new()
    {
        DeliveryAttemptTimeout = AttemptTimeout,
        Retry = new RetryOptions
        {
            MaxDispatches = maxDispatches,
            InitialDelay = InitialRetryDelay,
            MaxDelay = TimeSpan.FromHours(1),
            JitterFactor = 0,
        },
    };
}
