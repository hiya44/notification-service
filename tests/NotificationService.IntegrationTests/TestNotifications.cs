using NotificationService.Domain.Notifications;

namespace NotificationService.IntegrationTests;

internal static class TestNotifications
{
    public static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public static Notification Email(DateTimeOffset? createdAt = null, string? idempotencyKey = null) =>
        Notification.Create(
            CustomerId.Create("customer-1"),
            Recipient.Email(EmailAddress.Create("jane@example.com")),
            NotificationContent.Create("Your order has shipped", "It will arrive tomorrow."),
            createdAt ?? Now,
            idempotencyKey is null ? null : IdempotencyKey.Create(idempotencyKey));

    public static Notification Sms(DateTimeOffset? createdAt = null) =>
        Notification.Create(
            CustomerId.Create("customer-1"),
            Recipient.Sms(PhoneNumber.Create("+37060012345")),
            NotificationContent.Create(null, "Your code is 123456"),
            createdAt ?? Now);
}
