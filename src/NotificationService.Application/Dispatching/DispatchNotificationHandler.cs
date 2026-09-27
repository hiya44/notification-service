using Microsoft.Extensions.Logging;
using NotificationService.Domain.Common;
using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Dispatching;

/// <summary>
/// Dispatches one claimed notification as its own unit of work: load it, dispatch it, save the outcome.
/// </summary>
public sealed partial class DispatchNotificationHandler(
    INotificationRepository repository,
    NotificationDispatcher dispatcher,
    TimeProvider timeProvider,
    ILogger<DispatchNotificationHandler> logger)
{
    public async Task HandleAsync(NotificationId notificationId, CancellationToken cancellationToken)
    {
        var notification = await repository.GetAsync(notificationId, cancellationToken);

        // Re-checked after loading: if our lease expired, another instance may have dispatched it in the meantime.
        if (notification is null || !notification.IsDueAt(timeProvider.GetUtcNow()))
        {
            LogNotDue(logger, notificationId);
            return;
        }

        await dispatcher.DispatchAsync(notification, cancellationToken);

        try
        {
            await repository.UpdateAsync(notification, cancellationToken);
        }
        catch (ConcurrencyConflictException exception)
        {
            // Another instance processed it after our lease expired. Its result wins; ours is discarded.
            // If a provider accepted our attempt, the customer may receive the notification twice (at-least-once delivery).
            LogConcurrencyConflict(logger, exception, notificationId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification {NotificationId} is no longer due; skipped")]
    private static partial void LogNotDue(ILogger logger, NotificationId notificationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification {NotificationId} was changed by another process while it was dispatched; outcome discarded")]
    private static partial void LogConcurrencyConflict(ILogger logger, Exception exception, NotificationId notificationId);
}
