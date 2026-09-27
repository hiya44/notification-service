using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Providers;

/// <summary>
/// What a provider needs to deliver a notification. Providers receive this instead of the
/// <see cref="Notification"/> aggregate so they cannot change its state.
/// </summary>
/// <param name="NotificationId">Can be passed to providers that support idempotency or reference ids.</param>
public sealed record DeliveryRequest(NotificationId NotificationId, Recipient Recipient, NotificationContent Content)
{
    public Channel Channel => Recipient.Channel;

    public static DeliveryRequest For(Notification notification) =>
        new(notification.Id, notification.Recipient, notification.Content);
}
