using System.Text.RegularExpressions;
using NotificationService.Domain.Common;

namespace NotificationService.Domain.Notifications;

/// <summary>
/// A phone number in E.164 format (e.g. +37060012345), which SMS providers expect.
/// Common separators (spaces, dashes, parentheses) are removed before validation.
/// </summary>
public sealed partial record PhoneNumber
{
    public string Value { get; }

    private PhoneNumber(string value) => Value = value;

    public static PhoneNumber Create(string? value)
    {
        var normalized = value is null ? string.Empty : Separators().Replace(value, string.Empty);

        if (!E164().IsMatch(normalized))
        {
            throw new DomainException("Phone number must be in E.164 format, e.g. +37060012345.");
        }

        return new PhoneNumber(normalized);
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"[\s\-()]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^\+[1-9]\d{6,14}$")]
    private static partial Regex E164();
}
