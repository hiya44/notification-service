using Microsoft.Extensions.Options;
using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Providers;

/// <summary>
/// Decides which providers may deliver a notification on a channel, and in which order they are tried.
/// A provider is eligible when it is configured, enabled, configured for the channel and supports it.
/// </summary>
public sealed class ProviderSelector(
    IEnumerable<INotificationProvider> providers,
    IOptionsMonitor<NotificationOptions> options)
{
    private readonly IReadOnlyList<INotificationProvider> _providers = providers.ToList();

    /// <returns>Eligible providers ordered by priority (then by name, so the order is always deterministic).</returns>
    public IReadOnlyList<INotificationProvider> EligibleProvidersFor(Channel channel)
    {
        var configuration = options.CurrentValue.Providers;

        return _providers
            .Select(provider => (Provider: provider, Settings: configuration.GetValueOrDefault(provider.Name)))
            .Where(candidate =>
                candidate.Settings is { Enabled: true }
                && candidate.Settings.Channels.Contains(channel)
                && candidate.Provider.SupportedChannels.Contains(channel))
            .OrderBy(candidate => candidate.Settings!.Priority)
            .ThenBy(candidate => candidate.Provider.Name, StringComparer.Ordinal)
            .Select(candidate => candidate.Provider)
            .ToList();
    }
}
