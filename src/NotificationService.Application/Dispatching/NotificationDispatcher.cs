using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Application.Providers;
using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Dispatching;

/// <summary>
/// Performs one dispatch of a notification: tries the eligible providers in priority order until one delivers it.
/// If none does, the notification's next dispatch is scheduled according to the retry policy.
/// The dispatcher only changes the notification in memory; saving it is the caller's responsibility.
/// </summary>
public sealed partial class NotificationDispatcher(
    ProviderSelector providerSelector,
    IOptionsMonitor<DispatchOptions> options,
    TimeProvider timeProvider,
    ILogger<NotificationDispatcher> logger)
{
    public async Task DispatchAsync(Notification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        // Checked up front: failing later would mean a provider had already sent the notification.
        if (notification.Status != NotificationStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Notification {notification.Id} is {notification.Status} and cannot be dispatched.");
        }

        var settings = options.CurrentValue;
        var providers = providerSelector.EligibleProvidersFor(notification.Channel);

        if (providers.Count == 0)
        {
            LogNoEligibleProvider(logger, notification.Id, notification.Channel);
            ScheduleRetryOrFail(notification, settings, $"No eligible provider for channel {notification.Channel}.");
            return;
        }

        var request = DeliveryRequest.For(notification);

        foreach (var provider in providers)
        {
            var outcome = await AttemptDeliveryAsync(provider, request, settings.DeliveryAttemptTimeout, cancellationToken);
            notification.RecordDeliveryAttempt(provider.Name, outcome, timeProvider.GetUtcNow());

            if (outcome.Kind != DeliveryOutcomeKind.TransientFailure)
            {
                LogDispatchCompleted(logger, notification.Id, provider.Name, outcome.Kind);
                return;
            }

            LogProviderFailed(logger, notification.Id, provider.Name, outcome.FailureReason);
        }

        ScheduleRetryOrFail(notification, settings, "All eligible providers failed.");
    }

    private async Task<DeliveryOutcome> AttemptDeliveryAsync(
        INotificationProvider provider,
        DeliveryRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        // Cancelled when the attempt times out, so a cooperative provider can stop its work.
        using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            // WaitAsync enforces the timeout even if the provider ignores the cancellation token.
            return await provider
                .SendAsync(request, attemptCancellation.Token)
                .WaitAsync(timeout, timeProvider, cancellationToken);
        }
        catch (TimeoutException)
        {
            await attemptCancellation.CancelAsync();
            return DeliveryOutcome.TransientFailure($"No response within {timeout}.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A misbehaving provider must not stop the others from being tried.
            // Only the exception type is stored; details (which may contain personal data) go to the logs.
            LogProviderThrew(logger, exception, request.NotificationId, provider.Name);
            return DeliveryOutcome.TransientFailure($"Unexpected {exception.GetType().Name} from provider.");
        }
    }

    private void ScheduleRetryOrFail(Notification notification, DispatchOptions settings, string reason)
    {
        notification.ScheduleRetryOrFail(settings.Retry.ToPolicy(), reason, timeProvider.GetUtcNow());

        if (notification.Status == NotificationStatus.Failed)
        {
            LogGaveUp(logger, notification.Id, notification.FailedDispatchCount, reason);
        }
        else
        {
            LogRetryScheduled(logger, notification.Id, notification.NextAttemptAt, reason);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification {NotificationId}: no eligible provider for channel {Channel}")]
    private static partial void LogNoEligibleProvider(ILogger logger, NotificationId notificationId, Channel channel);

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification {NotificationId}: provider {Provider} reported {Outcome}")]
    private static partial void LogDispatchCompleted(ILogger logger, NotificationId notificationId, string provider, DeliveryOutcomeKind outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification {NotificationId}: provider {Provider} failed transiently: {Reason}")]
    private static partial void LogProviderFailed(ILogger logger, NotificationId notificationId, string provider, string? reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification {NotificationId}: provider {Provider} threw an unexpected exception")]
    private static partial void LogProviderThrew(ILogger logger, Exception exception, NotificationId notificationId, string provider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification {NotificationId}: not delivered ({Reason}); next dispatch at {NextAttemptAt}")]
    private static partial void LogRetryScheduled(ILogger logger, NotificationId notificationId, DateTimeOffset? nextAttemptAt, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Notification {NotificationId}: gave up after {FailedDispatches} dispatches ({Reason})")]
    private static partial void LogGaveUp(ILogger logger, NotificationId notificationId, int failedDispatches, string reason);
}
