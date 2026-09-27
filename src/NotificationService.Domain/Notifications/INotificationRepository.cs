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

    /// <summary>
    /// Claims up to <paramref name="maxCount"/> notifications that are due at <paramref name="now"/>, earliest first,
    /// so that no other dispatcher instance picks them up for <paramref name="leaseDuration"/>.
    /// If the claiming instance crashes, the lease expires and the notifications become claimable again.
    /// </summary>
    Task<IReadOnlyList<Notification>> ClaimDueAsync(
        DateTimeOffset now,
        int maxCount,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves changes to a notification loaded through this repository and releases its claim.
    /// </summary>
    /// <exception cref="Common.ConcurrencyConflictException">Another process changed the notification in the meantime.</exception>
    Task UpdateAsync(Notification notification, CancellationToken cancellationToken);
}
