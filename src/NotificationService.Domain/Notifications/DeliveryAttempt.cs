namespace NotificationService.Domain.Notifications;

/// <summary>
/// A record of one provider being asked to deliver a notification, and what happened.
/// </summary>
public sealed record DeliveryAttempt(
    string ProviderName,
    DeliveryOutcomeKind Outcome,
    string? FailureReason,
    string? ProviderMessageId,
    DateTimeOffset AttemptedAt);
