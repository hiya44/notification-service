using Microsoft.EntityFrameworkCore;
using Npgsql;
using NotificationService.Domain.Common;
using NotificationService.Domain.Notifications;
using NotificationService.Infrastructure.Persistence.Configurations;

namespace NotificationService.Infrastructure.Persistence;

public sealed class NotificationRepository(NotificationDbContext dbContext) : INotificationRepository
{
    public async Task<bool> TryAddAsync(Notification notification, CancellationToken cancellationToken)
    {
        dbContext.Notifications.Add(notification);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsIdempotencyKeyViolation(exception))
        {
            dbContext.Entry(notification).State = EntityState.Detached;
            return false;
        }
    }

    public Task<Notification?> GetAsync(NotificationId id, CancellationToken cancellationToken) =>
        dbContext.Notifications.SingleOrDefaultAsync(n => n.Id == id, cancellationToken);

    public Task<Notification?> FindByIdempotencyKeyAsync(IdempotencyKey idempotencyKey, CancellationToken cancellationToken) =>
        dbContext.Notifications.SingleOrDefaultAsync(n => n.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<IReadOnlyList<Notification>> ClaimDueAsync(
        DateTimeOffset now,
        int maxCount,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var lockedUntil = now + leaseDuration;

        // One atomic statement: select due, unclaimed rows and claim them by setting a lease.
        // FOR UPDATE SKIP LOCKED lets concurrent instances claim different rows instead of waiting on each other.
        // Row locks last only for this statement; the lease is what protects the (slow) dispatch that follows.
        var claimedIds = await dbContext.Database
            .SqlQuery<Guid>($"""
                UPDATE notifications
                SET locked_until = {lockedUntil}
                WHERE id IN (
                    SELECT id
                    FROM notifications
                    WHERE status = 'Pending'
                      AND next_attempt_at <= {now}
                      AND (locked_until IS NULL OR locked_until <= {now})
                    ORDER BY next_attempt_at
                    LIMIT {maxCount}
                    FOR UPDATE SKIP LOCKED)
                RETURNING id AS "Value"
                """)
            .ToListAsync(cancellationToken);

        if (claimedIds.Count == 0)
        {
            return [];
        }

        // Loaded with LINQ rather than raw SQL so EF applies the configured column mapping
        // (complex types and the xmin concurrency token). Translates to "WHERE id = ANY(@ids)".
        var ids = claimedIds.Select(id => new NotificationId(id)).ToList();
        var claimed = await dbContext.Notifications
            .Where(n => ids.Contains(n.Id))
            .ToListAsync(cancellationToken);

        return claimed.OrderBy(n => n.NextAttemptAt).ToList();
    }

    public async Task UpdateAsync(Notification notification, CancellationToken cancellationToken)
    {
        var entry = dbContext.Entry(notification);
        if (entry.State == EntityState.Detached)
        {
            throw new InvalidOperationException("Only notifications loaded through this repository can be updated.");
        }

        entry.Property(NotificationConfiguration.LockedUntil).CurrentValue = null;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException(
                $"Notification {notification.Id} was changed by another process.", exception);
        }
    }

    private static bool IsIdempotencyKeyViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: NotificationConfiguration.IdempotencyKeyIndexName,
        };
}
