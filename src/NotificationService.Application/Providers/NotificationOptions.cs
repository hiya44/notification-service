namespace NotificationService.Application.Providers;

/// <summary>
/// Configuration of the notification providers, keyed by provider name.
/// Bound from the "Notifications" configuration section and re-read on every dispatch,
/// so providers can be disabled or re-prioritised without a restart.
/// </summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    public Dictionary<string, ProviderOptions> Providers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
