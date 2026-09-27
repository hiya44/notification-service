using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Tests;

internal static class TestNotifications
{
    public static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public static Notification Sms(DateTimeOffset? createdAt = null) =>
        Notification.Create(
            CustomerId.Create("customer-1"),
            Recipient.Sms(PhoneNumber.Create("+37060012345")),
            NotificationContent.Create(null, "Your code is 123456"),
            createdAt ?? Now);
}
