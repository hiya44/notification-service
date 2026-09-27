using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Application.Providers;
using NotificationService.Domain.Notifications;

namespace NotificationService.Infrastructure.Providers.Simulated;

/// <summary>
/// Stands in for a third-party provider whose API is out of scope for this service (no accounts or credentials).
/// Nothing is sent; the outcome follows the configured <see cref="SimulatedProviderOptions"/>, so failover,
/// timeouts and retries can be demonstrated. A real integration would replace a subclass with an HTTP client
/// that maps the provider's responses to <see cref="DeliveryOutcome"/>s.
/// </summary>
public abstract partial class SimulatedProvider : INotificationProvider
{
    private readonly IOptionsMonitor<SimulatedProviderOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Random _random;

    protected SimulatedProvider(
        string name,
        IReadOnlySet<Channel> supportedChannels,
        IOptionsMonitor<SimulatedProviderOptions> options,
        TimeProvider timeProvider,
        ILogger logger,
        Random? random = null)
    {
        Name = name;
        SupportedChannels = supportedChannels;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _random = random ?? Random.Shared;
    }

    public string Name { get; }

    public IReadOnlySet<Channel> SupportedChannels { get; }

    public async Task<DeliveryOutcome> SendAsync(DeliveryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!SupportedChannels.Contains(request.Channel))
        {
            throw new ArgumentException($"{Name} does not support channel {request.Channel}.", nameof(request));
        }

        var settings = _options.Get(Name);

        if (settings.Latency > TimeSpan.Zero)
        {
            await Task.Delay(settings.Latency, _timeProvider, cancellationToken);
        }

        var outcome = settings.Mode switch
        {
            SimulationMode.AlwaysFail => DeliveryOutcome.TransientFailure($"Simulated {Name} outage."),
            SimulationMode.PermanentFailure => DeliveryOutcome.PermanentFailure($"Simulated: {Name} rejected the recipient."),
            _ when _random.NextDouble() < settings.FailureRate => DeliveryOutcome.TransientFailure($"Simulated {Name} error."),
            _ => DeliveryOutcome.Delivered($"{Name.ToLowerInvariant()}-{Guid.NewGuid():N}"),
        };

        LogSimulatedAttempt(_logger, Name, request.NotificationId, request.Channel, outcome.Kind);
        return outcome;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Simulated provider {Provider}: notification {NotificationId} on {Channel} -> {Outcome}")]
    private static partial void LogSimulatedAttempt(ILogger logger, string provider, NotificationId notificationId, Channel channel, DeliveryOutcomeKind outcome);
}
