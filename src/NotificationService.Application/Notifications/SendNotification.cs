using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Notifications;

/// <summary>A request from another service to notify a customer.</summary>
public sealed record SendNotificationCommand(
    string? CustomerId,
    Channel Channel,
    string? Recipient,
    string? Subject,
    string? Body,
    string? IdempotencyKey = null);

/// <param name="NotificationId">The accepted notification.</param>
/// <param name="IsDuplicate">True when an earlier request with the same idempotency key already created it.</param>
public sealed record SendNotificationResult(NotificationId NotificationId, bool IsDuplicate);
