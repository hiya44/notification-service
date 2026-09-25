namespace NotificationService.Domain.Notifications;

public enum DeliveryOutcomeKind
{
    /// <summary>The provider accepted the notification.</summary>
    Delivered = 1,

    /// <summary>
    /// The provider could not deliver right now (outage, timeout, throttling, provider-specific rejection).
    /// Another provider, or a later retry, may succeed.
    /// </summary>
    TransientFailure = 2,

    /// <summary>
    /// The notification itself cannot be delivered by any provider (e.g. the recipient address does not exist).
    /// Retrying would not help.
    /// </summary>
    PermanentFailure = 3,
}

/// <summary>
/// The result a provider reports for a single delivery attempt.
/// </summary>
public sealed record DeliveryOutcome
{
    public DeliveryOutcomeKind Kind { get; }

    public string? FailureReason { get; }

    /// <summary>The provider's own message id, when delivered. Useful for tracing with the provider.</summary>
    public string? ProviderMessageId { get; }

    private DeliveryOutcome(DeliveryOutcomeKind kind, string? failureReason, string? providerMessageId)
    {
        Kind = kind;
        FailureReason = failureReason;
        ProviderMessageId = providerMessageId;
    }

    public bool IsDelivered => Kind == DeliveryOutcomeKind.Delivered;

    public static DeliveryOutcome Delivered(string? providerMessageId = null) =>
        new(DeliveryOutcomeKind.Delivered, null, providerMessageId);

    public static DeliveryOutcome TransientFailure(string reason) =>
        new(DeliveryOutcomeKind.TransientFailure, RequireReason(reason), null);

    public static DeliveryOutcome PermanentFailure(string reason) =>
        new(DeliveryOutcomeKind.PermanentFailure, RequireReason(reason), null);

    private static string RequireReason(string reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException("A failure reason is required.", nameof(reason))
            : reason;
}
