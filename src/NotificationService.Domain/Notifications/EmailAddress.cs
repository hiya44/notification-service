using System.Net.Mail;
using NotificationService.Domain.Common;

namespace NotificationService.Domain.Notifications;

public sealed record EmailAddress
{
    public const int MaxLength = 254;

    public string Value { get; }

    private EmailAddress(string value) => Value = value;

    public static EmailAddress Create(string? value)
    {
        var candidate = value?.Trim();

        // MailAddress also accepts display-name forms ("John <john@example.com>");
        // requiring the parsed address to equal the input rejects those.
        if (string.IsNullOrEmpty(candidate)
            || candidate.Length > MaxLength
            || !MailAddress.TryCreate(candidate, out var parsed)
            || parsed.Address != candidate)
        {
            throw new DomainException("Email address is not valid.");
        }

        return new EmailAddress(candidate);
    }

    public override string ToString() => Value;
}
