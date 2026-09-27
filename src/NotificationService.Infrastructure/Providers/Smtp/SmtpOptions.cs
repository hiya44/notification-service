using MailKit.Security;

namespace NotificationService.Infrastructure.Providers.Smtp;

/// <summary>
/// Connection settings for the SMTP email provider. Bound from the "Notifications:Smtp" configuration section.
/// Defaults point to the local Mailpit container from docker-compose.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Notifications:Smtp";

    public string Host { get; init; } = "localhost";

    public int Port { get; init; } = 1025;

    /// <summary>
    /// <see cref="SecureSocketOptions.Auto"/> uses TLS on port 465 and STARTTLS elsewhere when the server offers it.
    /// Use <see cref="SecureSocketOptions.StartTls"/> in production to refuse unencrypted connections.
    /// </summary>
    public SecureSocketOptions Security { get; init; } = SecureSocketOptions.Auto;

    /// <summary>Leave empty for servers without authentication (e.g. Mailpit).</summary>
    public string? Username { get; init; }

    public string? Password { get; init; }

    public string FromAddress { get; init; } = "notifications@example.com";

    public string? FromName { get; init; } = "Notification Service";
}
