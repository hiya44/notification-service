using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Tests.Fakes;

internal sealed class InMemoryNotificationRepository : INotificationRepository
{
    private readonly List<Notification> _notifications = [];

    public IReadOnlyList<Notification> Notifications => _notifications;

    /// <summary>
    /// Simulates a concurrent request: this notification is stored just before the next <see cref="TryAddAsync"/>.
    /// </summary>
    public Notification? ConcurrentlyAdded { get; set; }

    public Task<bool> TryAddAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (ConcurrentlyAdded is not null)
        {
            _notifications.Add(ConcurrentlyAdded);
            ConcurrentlyAdded = null;
        }

        var isDuplicate = notification.IdempotencyKey is not null
            && _notifications.Any(n => n.IdempotencyKey == notification.IdempotencyKey);

        if (!isDuplicate)
        {
            _notifications.Add(notification);
        }

        return Task.FromResult(!isDuplicate);
    }

    public Task<Notification?> GetAsync(NotificationId id, CancellationToken cancellationToken) =>
        Task.FromResult(_notifications.SingleOrDefault(n => n.Id == id));

    public Task<Notification?> FindByIdempotencyKeyAsync(IdempotencyKey idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(_notifications.SingleOrDefault(n => n.IdempotencyKey == idempotencyKey));

    public Task<IReadOnlyList<Notification>> ClaimDueAsync(
        DateTimeOffset now,
        int maxCount,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Notification> due = _notifications
            .Where(n => n.IsDueAt(now))
            .OrderBy(n => n.NextAttemptAt)
            .Take(maxCount)
            .ToList();

        return Task.FromResult(due);
    }

    public Task UpdateAsync(Notification notification, CancellationToken cancellationToken) => Task.CompletedTask;
}
