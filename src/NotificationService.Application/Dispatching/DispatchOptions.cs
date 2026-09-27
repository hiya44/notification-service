namespace NotificationService.Application.Dispatching;

/// <summary>
/// How notifications are dispatched. Bound from the "Notifications:Dispatch" configuration section
/// and re-read on every dispatch.
/// </summary>
public sealed class DispatchOptions
{
    public const string SectionName = "Notifications:Dispatch";

    /// <summary>How long a single provider may take before it is treated as a transient failure.</summary>
    public TimeSpan DeliveryAttemptTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public RetryOptions Retry { get; init; } = new();
}
