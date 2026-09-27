using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Notifications;

/// <summary>Read model describing a notification and its delivery history.</summary>
public sealed record NotificationDetails(
    NotificationId Id,
    string CustomerId,
    Channel Channel,
    string Recipient,
    NotificationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? DeliveredAt,
    string? FailureReason,
    int FailedDispatchCount,
    IReadOnlyList<DeliveryAttempt> DeliveryAttempts)
{
    public static NotificationDetails From(Notification notification) => new(
        notification.Id,
        notification.CustomerId.Value,
        notification.Channel,
        notification.Recipient.Address,
        notification.Status,
        notification.CreatedAt,
        notification.NextAttemptAt,
        notification.DeliveredAt,
        notification.FailureReason,
        notification.FailedDispatchCount,
        notification.DeliveryAttempts.ToList());
}
