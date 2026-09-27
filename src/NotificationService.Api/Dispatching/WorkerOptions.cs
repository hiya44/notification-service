namespace NotificationService.Api.Dispatching;

/// <summary>How the dispatch worker polls for due notifications. Bound from the "Notifications:Worker" configuration section.</summary>
public sealed class WorkerOptions
{
    public const string SectionName = "Notifications:Worker";

    /// <summary>How often to look for due notifications when the previous poll found less than a full batch.</summary>
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Maximum notifications claimed per poll. They are dispatched concurrently.</summary>
    public int BatchSize { get; init; } = 20;

    /// <summary>
    /// How long a claimed notification is reserved for this instance. Must exceed the longest possible dispatch
    /// (delivery attempt timeout x eligible providers); otherwise another instance may dispatch it a second time.
    /// </summary>
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(2);
}
