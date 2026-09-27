using Microsoft.Extensions.Options;
using NotificationService.Application.Dispatching;
using NotificationService.Application.Providers;

namespace NotificationService.Api.Dispatching;

/// <summary>
/// Checks the worker settings, including that a lease outlasts the longest possible dispatch:
/// every eligible provider for a channel timing out one after another.
/// </summary>
public sealed class WorkerOptionsValidator(
    IOptionsMonitor<DispatchOptions> dispatchOptions,
    IOptionsMonitor<NotificationOptions> notificationOptions) : IValidateOptions<WorkerOptions>
{
    public const int MaxBatchSize = 500;

    public ValidateOptionsResult Validate(string? name, WorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.PollingInterval <= TimeSpan.Zero)
        {
            failures.Add($"{WorkerOptions.SectionName}:PollingInterval must be positive.");
        }

        if (options.BatchSize is < 1 or > MaxBatchSize)
        {
            failures.Add($"{WorkerOptions.SectionName}:BatchSize must be between 1 and {MaxBatchSize}.");
        }

        var longestDispatch = dispatchOptions.CurrentValue.DeliveryAttemptTimeout * Math.Max(1, MaxProvidersPerChannel());
        if (options.LeaseDuration <= longestDispatch)
        {
            failures.Add(
                $"{WorkerOptions.SectionName}:LeaseDuration ({options.LeaseDuration}) must be longer than the longest possible " +
                $"dispatch ({longestDispatch}: delivery attempt timeout x enabled providers for a channel).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private int MaxProvidersPerChannel()
    {
        var enabled = notificationOptions.CurrentValue.Providers.Values.Where(provider => provider.Enabled).ToList();

        return enabled.Count == 0
            ? 0
            : enabled.SelectMany(provider => provider.Channels).GroupBy(channel => channel).Max(group => group.Count());
    }
}
