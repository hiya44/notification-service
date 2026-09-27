using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Domain.Notifications;

namespace NotificationService.Infrastructure.Providers.Simulated;

/// <summary>Simulated Twilio (SMS).</summary>
public sealed class TwilioProvider(
    IOptionsMonitor<SimulatedProviderOptions> options,
    TimeProvider timeProvider,
    ILogger<TwilioProvider> logger)
    : SimulatedProvider(ProviderName, new HashSet<Channel> { Channel.Sms }, options, timeProvider, logger)
{
    public const string ProviderName = "Twilio";
}
