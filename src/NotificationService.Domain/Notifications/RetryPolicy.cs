namespace NotificationService.Domain.Notifications;

/// <summary>
/// Decides whether and when a notification is dispatched again after a dispatch in which no provider delivered it.
/// Delays grow exponentially (initial, 2x, 4x, ...) up to a maximum, with random jitter so that notifications
/// that failed together during a provider outage do not all retry at the same moment.
/// </summary>
public sealed class RetryPolicy
{
    private readonly Func<double> _randomSource;

    /// <param name="maxDispatches">Total number of dispatches allowed, including the first one.</param>
    /// <param name="initialDelay">Delay before the first retry.</param>
    /// <param name="maxDelay">Upper bound for any single delay.</param>
    /// <param name="jitterFactor">Relative jitter in [0, 1): 0.2 spreads each delay by up to ±20%.</param>
    /// <param name="randomSource">Returns a value in [0, 1). Replaceable for deterministic tests.</param>
    public RetryPolicy(
        int maxDispatches,
        TimeSpan initialDelay,
        TimeSpan maxDelay,
        double jitterFactor = 0.2,
        Func<double>? randomSource = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDispatches, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(initialDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDelay, initialDelay);
        ArgumentOutOfRangeException.ThrowIfNegative(jitterFactor);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(jitterFactor, 1);

        MaxDispatches = maxDispatches;
        InitialDelay = initialDelay;
        MaxDelay = maxDelay;
        JitterFactor = jitterFactor;
        _randomSource = randomSource ?? Random.Shared.NextDouble;
    }

    /// <summary>
    /// 8 dispatches with delays of roughly 1, 2, 4, 8, 16, 32 and 60 minutes:
    /// a notification is given up on about two hours after it was first dispatched.
    /// </summary>
    public static RetryPolicy Default { get; } = new(
        maxDispatches: 8,
        initialDelay: TimeSpan.FromMinutes(1),
        maxDelay: TimeSpan.FromHours(1));

    public int MaxDispatches { get; }

    public TimeSpan InitialDelay { get; }

    public TimeSpan MaxDelay { get; }

    public double JitterFactor { get; }

    public bool AllowsRetryAfter(int failedDispatches) => failedDispatches < MaxDispatches;

    /// <summary>
    /// The delay before the next dispatch, given how many dispatches have failed so far (1 or more).
    /// </summary>
    public TimeSpan DelayAfter(int failedDispatches)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedDispatches, 1);

        // Exponent is capped to avoid overflow; the delay is capped by MaxDelay long before that matters.
        var exponent = Math.Min(failedDispatches - 1, 30);
        var exponential = InitialDelay.TotalMilliseconds * Math.Pow(2, exponent);
        var capped = Math.Min(exponential, MaxDelay.TotalMilliseconds);

        // Scale by a random factor in [1 - jitter, 1 + jitter), never exceeding MaxDelay.
        var jitterMultiplier = 1 + (JitterFactor * ((2 * _randomSource()) - 1));
        var jittered = Math.Min(capped * jitterMultiplier, MaxDelay.TotalMilliseconds);

        return TimeSpan.FromMilliseconds(jittered);
    }
}
