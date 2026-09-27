using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Domain.Notifications;

namespace NotificationService.Infrastructure.Providers.Simulated;

/// <summary>Simulated Amazon Simple Email Service (email).</summary>
public sealed class AmazonSesProvider(
    IOptionsMonitor<SimulatedProviderOptions> options,
    TimeProvider timeProvider,
    ILogger<AmazonSesProvider> logger)
    : SimulatedProvider(ProviderName, new HashSet<Channel> { Channel.Email }, options, timeProvider, logger)
{
    public const string ProviderName = "AmazonSes";
}
