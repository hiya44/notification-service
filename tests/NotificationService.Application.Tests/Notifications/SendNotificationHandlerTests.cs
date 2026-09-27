using Microsoft.Extensions.Time.Testing;
using NotificationService.Application.Notifications;
using NotificationService.Application.Tests.Fakes;
using NotificationService.Domain.Common;
using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Tests.Notifications;

public class SendNotificationHandlerTests
{
    private readonly InMemoryNotificationRepository _repository = new();
    private readonly FakeTimeProvider _time = new(TestNotifications.Now);
    private readonly SendNotificationHandler _handler;

    public SendNotificationHandlerTests() => _handler = new SendNotificationHandler(_repository, _time);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_StoresPendingNotificationDueImmediately()
    {
        var result = await _handler.HandleAsync(EmailCommand(), CancellationToken);

        result.IsDuplicate.ShouldBeFalse();
        var stored = _repository.Notifications.ShouldHaveSingleItem();
        stored.Id.ShouldBe(result.NotificationId);
        stored.Status.ShouldBe(NotificationStatus.Pending);
        stored.Channel.ShouldBe(Channel.Email);
        stored.Recipient.Address.ShouldBe("jane@example.com");
        stored.Content.Subject.ShouldBe("Your order has shipped");
        stored.CreatedAt.ShouldBe(TestNotifications.Now);
        stored.IsDueAt(TestNotifications.Now).ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_WithInvalidRequest_ThrowsAndStoresNothing()
    {
        var command = EmailCommand() with { Recipient = "not-an-email" };

        await Should.ThrowAsync<DomainException>(() => _handler.HandleAsync(command, CancellationToken));

        _repository.Notifications.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WithoutIdempotencyKey_CreatesNotificationEveryTime()
    {
        await _handler.HandleAsync(EmailCommand(), CancellationToken);
        await _handler.HandleAsync(EmailCommand(), CancellationToken);

        _repository.Notifications.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Handle_WithRepeatedIdempotencyKey_ReturnsOriginalNotification()
    {
        var command = EmailCommand() with { IdempotencyKey = "order-42-shipped" };

        var first = await _handler.HandleAsync(command, CancellationToken);
        var second = await _handler.HandleAsync(command, CancellationToken);

        second.NotificationId.ShouldBe(first.NotificationId);
        second.IsDuplicate.ShouldBeTrue();
        _repository.Notifications.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Handle_WithIdempotencyKeyReusedForDifferentRequest_Throws()
    {
        await _handler.HandleAsync(EmailCommand() with { IdempotencyKey = "order-42" }, CancellationToken);

        var different = EmailCommand() with { IdempotencyKey = "order-42", Body = "Something else" };

        await Should.ThrowAsync<IdempotencyKeyConflictException>(() => _handler.HandleAsync(different, CancellationToken));
        _repository.Notifications.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Handle_WhenConcurrentRequestWithSameKeyWins_ReturnsWinningNotification()
    {
        var command = EmailCommand() with { IdempotencyKey = "order-42-shipped" };
        var winner = Notification.Create(
            CustomerId.Create(command.CustomerId),
            Recipient.Create(command.Channel, command.Recipient),
            NotificationContent.Create(command.Subject, command.Body),
            TestNotifications.Now,
            IdempotencyKey.Create(command.IdempotencyKey));
        _repository.ConcurrentlyAdded = winner;

        var result = await _handler.HandleAsync(command, CancellationToken);

        result.NotificationId.ShouldBe(winner.Id);
        result.IsDuplicate.ShouldBeTrue();
        _repository.Notifications.ShouldHaveSingleItem();
    }

    private static SendNotificationCommand EmailCommand() => new(
        CustomerId: "customer-1",
        Channel: Channel.Email,
        Recipient: "jane@example.com",
        Subject: "Your order has shipped",
        Body: "It will arrive tomorrow.");
}
