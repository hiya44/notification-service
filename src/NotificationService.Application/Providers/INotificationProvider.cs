using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Providers;

/// <summary>
/// Port for an external service that delivers notifications (e.g. Twilio, Amazon SES).
/// Implementations translate a <see cref="DeliveryRequest"/> into the provider's API call
/// and classify the result as a <see cref="DeliveryOutcome"/>.
/// </summary>
public interface INotificationProvider
{
    /// <summary>Unique name, also used as the provider's key in configuration.</summary>
    string Name { get; }

    /// <summary>Channels this provider is technically able to deliver on.</summary>
    IReadOnlySet<Channel> SupportedChannels { get; }

    /// <summary>
    /// Asks the provider to deliver the notification. Expected failures should be returned as an outcome;
    /// unexpected exceptions are treated as transient failures by the caller.
    /// </summary>
    Task<DeliveryOutcome> SendAsync(DeliveryRequest request, CancellationToken cancellationToken);
}
