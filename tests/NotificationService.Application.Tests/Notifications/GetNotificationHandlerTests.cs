using NotificationService.Application.Notifications;
using NotificationService.Application.Tests.Fakes;
using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Tests.Notifications;

public class GetNotificationHandlerTests
{
    private readonly InMemoryNotificationRepository _repository = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_ReturnsDetailsIncludingDeliveryHistory()
    {
        var notification = TestNotifications.Sms();
        notification.RecordDeliveryAttempt("Twilio", DeliveryOutcome.TransientFailure("Timeout"), TestNotifications.Now);
        notification.RecordDeliveryAttempt("Vonage", DeliveryOutcome.Delivered("V-1"), TestNotifications.Now);
        await _repository.TryAddAsync(notification, CancellationToken);

        var details = await new GetNotificationHandler(_repository).HandleAsync(notification.Id, CancellationToken);

        details.ShouldNotBeNull();
        details.Id.ShouldBe(notification.Id);
        details.CustomerId.ShouldBe("customer-1");
        details.Channel.ShouldBe(Channel.Sms);
        details.Recipient.ShouldBe("+37060012345");
        details.Status.ShouldBe(NotificationStatus.Delivered);
        details.DeliveryAttempts.Select(a => a.ProviderName).ShouldBe(new[] { "Twilio", "Vonage" });
    }

    [Fact]
    public async Task Handle_WhenNotificationDoesNotExist_ReturnsNull()
    {
        var details = await new GetNotificationHandler(_repository).HandleAsync(NotificationId.New(), CancellationToken);

        details.ShouldBeNull();
    }
}
