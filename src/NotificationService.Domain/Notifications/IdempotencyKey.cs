using NotificationService.Domain.Common;

namespace NotificationService.Domain.Notifications;

/// <summary>
/// A caller-chosen key (typically a GUID) identifying one logical request to send a notification.
/// Sending again with the same key returns the original notification instead of creating a new one.
/// </summary>
public sealed record IdempotencyKey
{
    public const int MaxLength = 100;

    public string Value { get; }

    private IdempotencyKey(string value) => Value = value;

    public static IdempotencyKey Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Idempotency key must not be blank.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
        {
            throw new DomainException($"Idempotency key must be at most {MaxLength} characters.");
        }

        return new IdempotencyKey(trimmed);
    }

    public override string ToString() => Value;
}
