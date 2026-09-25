using NotificationService.Domain.Common;

namespace NotificationService.Domain.Notifications;

/// <summary>
/// Identifies the customer a notification is about, as known by the calling service.
/// The Notification Service does not own customer data; the id is kept for traceability.
/// </summary>
public sealed record CustomerId
{
    public const int MaxLength = 128;

    public string Value { get; }

    private CustomerId(string value) => Value = value;

    public static CustomerId Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Customer id is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
        {
            throw new DomainException($"Customer id must be at most {MaxLength} characters.");
        }

        return new CustomerId(trimmed);
    }

    public override string ToString() => Value;
}
