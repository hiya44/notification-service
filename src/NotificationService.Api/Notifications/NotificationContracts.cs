using NotificationService.Application.Notifications;
using NotificationService.Domain.Notifications;

namespace NotificationService.Api.Notifications;

/// <summary>A request to notify a customer.</summary>
/// <param name="CustomerId">The customer's id in the calling service. Kept for traceability.</param>
/// <param name="Channel">How to deliver the notification: <c>Sms</c> or <c>Email</c>.</param>
/// <param name="Recipient">The phone number (E.164, e.g. +37060012345) or email address to deliver to.</param>
/// <param name="Subject">Required for email; not allowed for SMS.</param>
/// <param name="Body">The message text.</param>
public sealed record SendNotificationRequest(
    string? CustomerId,
    Channel? Channel,
    string? Recipient,
    string? Subject,
    string? Body);

/// <summary>Returned when a notification is accepted for delivery.</summary>
/// <param name="Id">Use it to query the notification's status.</param>
public sealed record SendNotificationResponse(Guid Id);

/// <summary>A notification and its delivery history.</summary>
public sealed record NotificationResponse(
    Guid Id,
    string CustomerId,
    Channel Channel,
    string Recipient,
    NotificationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? DeliveredAt,
    string? FailureReason,
    int FailedDispatchCount,
    IReadOnlyList<DeliveryAttemptResponse> DeliveryAttempts)
{
    public static NotificationResponse From(NotificationDetails details) => new(
        details.Id.Value,
        details.CustomerId,
        details.Channel,
        details.Recipient,
        details.Status,
        details.CreatedAt,
        details.NextAttemptAt,
        details.DeliveredAt,
        details.FailureReason,
        details.FailedDispatchCount,
        details.DeliveryAttempts.Select(DeliveryAttemptResponse.From).ToList());
}

/// <summary>One provider being asked to deliver the notification, and the outcome.</summary>
public sealed record DeliveryAttemptResponse(
    string Provider,
    DeliveryOutcomeKind Outcome,
    string? FailureReason,
    string? ProviderMessageId,
    DateTimeOffset AttemptedAt)
{
    public static DeliveryAttemptResponse From(DeliveryAttempt attempt) => new(
        attempt.ProviderName,
        attempt.Outcome,
        attempt.FailureReason,
        attempt.ProviderMessageId,
        attempt.AttemptedAt);
}
