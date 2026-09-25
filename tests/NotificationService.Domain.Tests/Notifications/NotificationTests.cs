using NotificationService.Domain.Common;
using NotificationService.Domain.Notifications;

namespace NotificationService.Domain.Tests.Notifications;

public class NotificationTests
{
    private static readonly DateTimeOffset Now = TestNotifications.Now;

    [Fact]
    public void Create_IsPendingAndDueImmediately()
    {
        var notification = TestNotifications.Email();

        notification.Status.ShouldBe(NotificationStatus.Pending);
        notification.Channel.ShouldBe(Channel.Email);
        notification.CreatedAt.ShouldBe(Now);
        notification.NextAttemptAt.ShouldBe(Now);
        notification.DeliveryAttempts.ShouldBeEmpty();
    }

    [Fact]
    public void Create_EmailWithoutSubject_Throws()
    {
        Should.Throw<DomainException>(() => Notification.Create(
            CustomerId.Create("customer-1"),
            Recipient.Email(EmailAddress.Create("jane@example.com")),
            NotificationContent.Create(null, "Body"),
            Now));
    }

    [Fact]
    public void Create_SmsWithSubject_Throws()
    {
        Should.Throw<DomainException>(() => Notification.Create(
            CustomerId.Create("customer-1"),
            Recipient.Sms(PhoneNumber.Create("+37060012345")),
            NotificationContent.Create("Subject", "Body"),
            Now));
    }

    [Fact]
    public void Create_SmsWithTooLongBody_Throws()
    {
        var body = new string('a', Notification.MaxSmsBodyLength + 1);

        Should.Throw<DomainException>(() => Notification.Create(
            CustomerId.Create("customer-1"),
            Recipient.Sms(PhoneNumber.Create("+37060012345")),
            NotificationContent.Create(null, body),
            Now));
    }

    [Fact]
    public void RecordDeliveryAttempt_Delivered_CompletesNotification()
    {
        var notification = TestNotifications.Sms();
        var attemptedAt = Now.AddSeconds(5);

        notification.RecordDeliveryAttempt("Twilio", DeliveryOutcome.Delivered("SM123"), attemptedAt);

        notification.Status.ShouldBe(NotificationStatus.Delivered);
        notification.DeliveredAt.ShouldBe(attemptedAt);
        notification.NextAttemptAt.ShouldBeNull();

        var attempt = notification.DeliveryAttempts.ShouldHaveSingleItem();
        attempt.ProviderName.ShouldBe("Twilio");
        attempt.Outcome.ShouldBe(DeliveryOutcomeKind.Delivered);
        attempt.ProviderMessageId.ShouldBe("SM123");
        attempt.AttemptedAt.ShouldBe(attemptedAt);
    }

    [Fact]
    public void RecordDeliveryAttempt_TransientFailure_StaysPending()
    {
        var notification = TestNotifications.Sms();

        notification.RecordDeliveryAttempt("Twilio", DeliveryOutcome.TransientFailure("Timeout"), Now);

        notification.Status.ShouldBe(NotificationStatus.Pending);
        notification.FailureReason.ShouldBeNull();

        var attempt = notification.DeliveryAttempts.ShouldHaveSingleItem();
        attempt.Outcome.ShouldBe(DeliveryOutcomeKind.TransientFailure);
        attempt.FailureReason.ShouldBe("Timeout");
    }

    [Fact]
    public void RecordDeliveryAttempt_PermanentFailure_FailsNotification()
    {
        var notification = TestNotifications.Sms();

        notification.RecordDeliveryAttempt("Twilio", DeliveryOutcome.PermanentFailure("Number does not exist"), Now);

        notification.Status.ShouldBe(NotificationStatus.Failed);
        notification.FailureReason.ShouldBe("Number does not exist");
        notification.NextAttemptAt.ShouldBeNull();
    }

    [Fact]
    public void RecordDeliveryAttempt_KeepsAttemptsInOrder()
    {
        var notification = TestNotifications.Sms();

        notification.RecordDeliveryAttempt("Twilio", DeliveryOutcome.TransientFailure("Timeout"), Now);
        notification.RecordDeliveryAttempt("Vonage", DeliveryOutcome.Delivered(), Now.AddSeconds(1));

        notification.DeliveryAttempts.Select(a => a.ProviderName).ShouldBe(new[] { "Twilio", "Vonage" });
        notification.Status.ShouldBe(NotificationStatus.Delivered);
    }

    [Fact]
    public void RecordDeliveryAttempt_WhenAlreadyDelivered_Throws()
    {
        var notification = TestNotifications.Sms();
        notification.RecordDeliveryAttempt("Twilio", DeliveryOutcome.Delivered(), Now);

        Should.Throw<DomainException>(() =>
            notification.RecordDeliveryAttempt("Vonage", DeliveryOutcome.Delivered(), Now));
    }

    [Fact]
    public void RecordDeliveryAttempt_WhenFailed_Throws()
    {
        var notification = TestNotifications.Sms();
        notification.RecordDeliveryAttempt("Twilio", DeliveryOutcome.PermanentFailure("Invalid number"), Now);

        Should.Throw<DomainException>(() =>
            notification.RecordDeliveryAttempt("Vonage", DeliveryOutcome.Delivered(), Now));
    }
}
