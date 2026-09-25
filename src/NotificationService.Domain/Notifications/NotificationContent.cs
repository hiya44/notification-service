using NotificationService.Domain.Common;

namespace NotificationService.Domain.Notifications;

/// <summary>
/// What the customer receives. The subject is optional here; whether it is required
/// depends on the channel and is enforced by <see cref="Notification"/>.
/// </summary>
public sealed record NotificationContent
{
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 10_000;

    public string? Subject { get; }

    public string Body { get; }

    private NotificationContent(string? subject, string body)
    {
        Subject = subject;
        Body = body;
    }

    public static NotificationContent Create(string? subject, string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new DomainException("Notification body is required.");
        }

        if (body.Length > MaxBodyLength)
        {
            throw new DomainException($"Notification body must be at most {MaxBodyLength} characters.");
        }

        var normalizedSubject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
        if (normalizedSubject?.Length > MaxSubjectLength)
        {
            throw new DomainException($"Notification subject must be at most {MaxSubjectLength} characters.");
        }

        return new NotificationContent(normalizedSubject, body);
    }
}
