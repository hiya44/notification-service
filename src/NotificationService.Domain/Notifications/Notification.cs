using NotificationService.Domain.Common;

namespace NotificationService.Domain.Notifications;

/// <summary>
/// Aggregate root: a request to inform a customer of something through a specific channel,
/// together with the history of attempts to deliver it.
/// </summary>
public sealed class Notification
{
    /// <summary>Roughly 10 concatenated SMS segments; longer messages are rejected by most providers.</summary>
    public const int MaxSmsBodyLength = 1_600;

    private readonly List<DeliveryAttempt> _deliveryAttempts = [];

    private Notification(
        NotificationId id,
        CustomerId customerId,
        Recipient recipient,
        NotificationContent content,
        DateTimeOffset createdAt)
    {
        Id = id;
        CustomerId = customerId;
        Recipient = recipient;
        Content = content;
        CreatedAt = createdAt;
        Status = NotificationStatus.Pending;
        NextAttemptAt = createdAt;
    }

    public NotificationId Id { get; }

    public CustomerId CustomerId { get; }

    public Recipient Recipient { get; }

    public Channel Channel => Recipient.Channel;

    public NotificationContent Content { get; }

    public NotificationStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>When the notification is next due for delivery. Null once it is Delivered or Failed.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public string? FailureReason { get; private set; }

    public IReadOnlyList<DeliveryAttempt> DeliveryAttempts => _deliveryAttempts.AsReadOnly();

    public static Notification Create(
        CustomerId customerId,
        Recipient recipient,
        NotificationContent content,
        DateTimeOffset now)
    {
        EnsureContentFitsChannel(recipient.Channel, content);

        return new Notification(NotificationId.New(), customerId, recipient, content, now);
    }

    /// <summary>
    /// Records the outcome of asking a provider to deliver this notification.
    /// A delivered outcome completes the notification; a permanent failure fails it;
    /// a transient failure is only recorded, so the caller can try another provider or retry later.
    /// </summary>
    public void RecordDeliveryAttempt(string providerName, DeliveryOutcome outcome, DateTimeOffset attemptedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(outcome);
        EnsurePending();

        _deliveryAttempts.Add(new DeliveryAttempt(
            providerName,
            outcome.Kind,
            outcome.FailureReason,
            outcome.ProviderMessageId,
            attemptedAt));

        switch (outcome.Kind)
        {
            case DeliveryOutcomeKind.Delivered:
                Status = NotificationStatus.Delivered;
                DeliveredAt = attemptedAt;
                NextAttemptAt = null;
                break;

            case DeliveryOutcomeKind.PermanentFailure:
                Fail(outcome.FailureReason!);
                break;

            case DeliveryOutcomeKind.TransientFailure:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome.Kind, "Unknown delivery outcome.");
        }
    }

    private void Fail(string reason)
    {
        Status = NotificationStatus.Failed;
        FailureReason = reason;
        NextAttemptAt = null;
    }

    private void EnsurePending()
    {
        if (Status != NotificationStatus.Pending)
        {
            throw new DomainException($"Notification {Id} is already {Status} and cannot be delivered again.");
        }
    }

    private static void EnsureContentFitsChannel(Channel channel, NotificationContent content)
    {
        switch (channel)
        {
            case Channel.Email when content.Subject is null:
                throw new DomainException("Email notifications require a subject.");

            case Channel.Sms when content.Subject is not null:
                throw new DomainException("SMS notifications cannot have a subject.");

            case Channel.Sms when content.Body.Length > MaxSmsBodyLength:
                throw new DomainException($"SMS body must be at most {MaxSmsBodyLength} characters.");
        }
    }
}
