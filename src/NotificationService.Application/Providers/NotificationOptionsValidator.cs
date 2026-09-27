using Microsoft.Extensions.Options;

namespace NotificationService.Application.Providers;

/// <summary>
/// Rejects provider configuration that can never work, so mistakes surface at startup (and on reload)
/// instead of as silently undelivered notifications: unknown provider names (typos), channels a provider
/// cannot deliver on, and invalid priorities.
/// </summary>
public sealed class NotificationOptionsValidator(IEnumerable<INotificationProvider> providers)
    : IValidateOptions<NotificationOptions>
{
    private readonly IReadOnlyDictionary<string, INotificationProvider> _providers =
        providers.ToDictionary(provider => provider.Name, StringComparer.OrdinalIgnoreCase);

    public ValidateOptionsResult Validate(string? name, NotificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        foreach (var (providerName, settings) in options.Providers)
        {
            if (!_providers.TryGetValue(providerName, out var provider))
            {
                failures.Add(
                    $"Provider '{providerName}' is configured but does not exist. " +
                    $"Known providers: {string.Join(", ", _providers.Keys.Order(StringComparer.Ordinal))}.");
                continue;
            }

            if (settings.Priority < 1)
            {
                failures.Add($"Provider '{providerName}' has priority {settings.Priority}; priorities start at 1.");
            }

            var unsupported = settings.Channels.Where(channel => !provider.SupportedChannels.Contains(channel)).ToList();
            if (unsupported.Count > 0)
            {
                failures.Add(
                    $"Provider '{providerName}' is configured for {string.Join(", ", unsupported)}, " +
                    $"but only supports {string.Join(", ", provider.SupportedChannels)}.");
            }

            if (settings.Enabled && settings.Channels.Count == 0)
            {
                failures.Add($"Provider '{providerName}' is enabled but not configured for any channel.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
