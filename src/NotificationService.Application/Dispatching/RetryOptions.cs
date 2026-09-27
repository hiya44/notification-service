using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Dispatching;

/// <summary>Configuration for the <see cref="RetryPolicy"/>. Defaults match <see cref="RetryPolicy.Default"/>.</summary>
public sealed class RetryOptions
{
    public int MaxDispatches { get; init; } = 8;

    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromMinutes(1);

    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromHours(1);

    public double JitterFactor { get; init; } = 0.2;

    public RetryPolicy ToPolicy() => new(MaxDispatches, InitialDelay, MaxDelay, JitterFactor);
}
