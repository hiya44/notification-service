using Microsoft.Extensions.Options;
using NotificationService.Application.Dispatching;
using NotificationService.Domain.Notifications;

namespace NotificationService.Api.Dispatching;

/// <summary>
/// Background worker that delivers accepted notifications: it polls for due notifications, claims a batch with a lease
/// (so several instances can run side by side) and dispatches each one in its own DI scope.
/// </summary>
public sealed partial class DispatchWorker(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<WorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<DispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger, options.CurrentValue.PollingInterval, options.CurrentValue.BatchSize);

        using var timer = new PeriodicTimer(options.CurrentValue.PollingInterval, timeProvider);

        do
        {
            try
            {
                // A full batch suggests more notifications are due (e.g. a backlog after an outage),
                // so keep claiming without waiting for the next tick.
                while (await ProcessDueNotificationsAsync(stoppingToken) >= options.CurrentValue.BatchSize)
                {
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                // E.g. the database is unavailable. Keep the worker alive and try again on the next tick.
                LogPollFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Claims one batch of due notifications and dispatches them.</summary>
    /// <returns>The number of notifications claimed.</returns>
    internal async Task<int> ProcessDueNotificationsAsync(CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;

        IReadOnlyList<NotificationId> claimed;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
            claimed = await repository.ClaimDueAsync(timeProvider.GetUtcNow(), settings.BatchSize, settings.LeaseDuration, cancellationToken);
        }

        if (claimed.Count == 0)
        {
            return 0;
        }

        LogClaimed(logger, claimed.Count);

        // Concurrently, because the lease is sized for one dispatch, not for a whole batch dispatched one after another.
        await Task.WhenAll(claimed.Select(id => DispatchAsync(id, cancellationToken)));

        return claimed.Count;
    }

    private async Task DispatchAsync(NotificationId notificationId, CancellationToken cancellationToken)
    {
        try
        {
            // Own scope, so each notification gets its own database context: a failed save cannot affect the others.
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<DispatchNotificationHandler>();
            await handler.HandleAsync(notificationId, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // One failing notification must not stop the batch. It keeps its lease, so it is retried when the lease expires.
            LogDispatchFailed(logger, exception, notificationId);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Dispatch worker started: polling every {PollingInterval}, batches of up to {BatchSize}")]
    private static partial void LogStarted(ILogger logger, TimeSpan pollingInterval, int batchSize);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Claimed {Count} due notifications")]
    private static partial void LogClaimed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Polling for due notifications failed; retrying on the next tick")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dispatching notification {NotificationId} failed; it will be retried when its lease expires")]
    private static partial void LogDispatchFailed(ILogger logger, Exception exception, NotificationId notificationId);
}
