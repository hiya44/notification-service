namespace NotificationService.Infrastructure.Providers.Simulated;

public enum SimulationMode
{
    /// <summary>Delivers, except for a random share of attempts given by <see cref="SimulatedProviderOptions.FailureRate"/>.</summary>
    Normal = 1,

    /// <summary>Every attempt fails transiently, as during an outage. Demonstrates failover and retries.</summary>
    AlwaysFail = 2,

    /// <summary>Every attempt fails permanently, as if the recipient's address did not exist.</summary>
    PermanentFailure = 3,
}

/// <summary>
/// How a simulated provider behaves. Bound per provider (as named options) from
/// "Notifications:Providers:{Name}:Simulation" and re-read on every attempt, so an outage
/// can be switched on and off at runtime.
/// </summary>
public sealed class SimulatedProviderOptions
{
    public const string SectionName = "Simulation";

    public SimulationMode Mode { get; init; } = SimulationMode.Normal;

    /// <summary>Share of attempts (0 to 1) that fail transiently in <see cref="SimulationMode.Normal"/> mode.</summary>
    public double FailureRate { get; init; }

    /// <summary>How long each attempt takes. Set above the delivery attempt timeout to simulate a hanging provider.</summary>
    public TimeSpan Latency { get; init; } = TimeSpan.FromMilliseconds(100);
}
