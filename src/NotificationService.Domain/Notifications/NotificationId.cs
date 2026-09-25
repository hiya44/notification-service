namespace NotificationService.Domain.Notifications;

public readonly record struct NotificationId(Guid Value)
{
    /// <summary>
    /// Version 7 GUIDs are time-ordered, which keeps database index inserts sequential.
    /// </summary>
    public static NotificationId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
