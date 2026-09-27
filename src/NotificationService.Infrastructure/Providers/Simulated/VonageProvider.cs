using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Domain.Notifications;

namespace NotificationService.Infrastructure.Providers.Simulated;

/// <summary>Simulated Vonage (SMS).</summary>
public sealed class VonageProvider(
    IOptionsMonitor<SimulatedProviderOptions> options,
    TimeProvider timeProvider,
    ILogger<VonageProvider> logger)
    : SimulatedProvider(ProviderName, new HashSet<Channel> { Channel.Sms }, options, timeProvider, logger)
{
    public const string ProviderName = "Vonage";
}
