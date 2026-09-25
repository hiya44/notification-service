namespace NotificationService.Domain.Notifications;

public enum NotificationStatus
{
    /// <summary>Accepted and waiting to be delivered (for the first time or as a retry).</summary>
    Pending = 1,

    /// <summary>A provider confirmed it accepted the notification for delivery.</summary>
    Delivered = 2,

    /// <summary>Delivery was given up on; no further attempts will be made.</summary>
    Failed = 3,
}
