using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Notifications;

/// <summary>
/// The idempotency key was already used for a request with different details.
/// Returning the earlier notification would hide a caller bug, so the request is rejected instead.
/// </summary>
public sealed class IdempotencyKeyConflictException(IdempotencyKey idempotencyKey)
    : Exception($"Idempotency key '{idempotencyKey}' was already used for a different notification.")
{
    public IdempotencyKey IdempotencyKey { get; } = idempotencyKey;
}
