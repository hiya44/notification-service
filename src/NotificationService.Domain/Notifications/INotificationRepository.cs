namespace NotificationService.Domain.Notifications;

/// <summary>
/// Stores and loads <see cref="Notification"/> aggregates. Each method persists its changes immediately.
/// </summary>
public interface INotificationRepository
{
    /// <summary>
    /// Stores a new notification. Returns false, without storing it, when a notification with the same
    /// idempotency key already exists (for example, created by a concurrent request).
    /// </summary>
    Task<bool> TryAddAsync(Notification notification, CancellationToken cancellationToken);

    Task<Notification?> GetAsync(NotificationId id, CancellationToken cancellationToken);

    Task<Notification?> FindByIdempotencyKeyAsync(IdempotencyKey idempotencyKey, CancellationToken cancellationToken);
}
