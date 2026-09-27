using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Utils;
using NotificationService.Application.Providers;
using NotificationService.Domain.Notifications;

namespace NotificationService.Infrastructure.Providers.Smtp;

/// <summary>
/// Delivers email through an SMTP server using MailKit. Locally this is Mailpit (see docker-compose),
/// so sent messages can be inspected at http://localhost:8025.
/// </summary>
public sealed partial class SmtpEmailProvider(
    IOptionsMonitor<SmtpOptions> options,
    ILogger<SmtpEmailProvider> logger) : INotificationProvider
{
    public const string ProviderName = "Smtp";

    /// <summary>Carries our notification id, so a message found in a mailbox or server log can be traced back.</summary>
    public const string NotificationIdHeader = "X-Notification-Id";

    public string Name => ProviderName;

    public IReadOnlySet<Channel> SupportedChannels { get; } = new HashSet<Channel> { Channel.Email };

    public async Task<DeliveryOutcome> SendAsync(DeliveryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Channel != Channel.Email)
        {
            throw new ArgumentException($"{Name} does not support channel {request.Channel}.", nameof(request));
        }

        var settings = options.CurrentValue;
        using var message = CreateMessage(request, settings);

        // A connection per message keeps the provider stateless and simple. At higher volume a pool of
        // authenticated connections (or the provider's HTTP API) would avoid the per-message handshake.
        using var client = new SmtpClient();

        try
        {
            await client.ConnectAsync(settings.Host, settings.Port, settings.Security, cancellationToken);

            if (!string.IsNullOrEmpty(settings.Username))
            {
                await client.AuthenticateAsync(settings.Username, settings.Password ?? string.Empty, cancellationToken);
            }

            var response = await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);

            LogSent(logger, request.NotificationId, response);
            return DeliveryOutcome.Delivered(message.MessageId);
        }
        catch (SmtpCommandException exception)
        {
            // The server's response text may echo the recipient's address, so only the status code is kept.
            LogRejected(logger, request.NotificationId, exception.ErrorCode, (int)exception.StatusCode);
            return Classify(exception);
        }
        catch (Exception exception) when (exception is SocketException or IOException or ProtocolException or AuthenticationException)
        {
            LogUnavailable(logger, exception, request.NotificationId);
            return DeliveryOutcome.TransientFailure($"SMTP server unavailable ({exception.GetType().Name}).");
        }
    }

    /// <summary>
    /// A 5xx rejection of the recipient (e.g. 550 mailbox unavailable) means this address cannot receive email
    /// through any provider. Everything else (4xx, rejected sender or message) is a problem with this server
    /// or its configuration, so another provider or a later retry may succeed.
    /// </summary>
    internal static DeliveryOutcome Classify(SmtpCommandException exception)
    {
        var statusCode = (int)exception.StatusCode;

        return exception.ErrorCode == SmtpErrorCode.RecipientNotAccepted && statusCode >= 500
            ? DeliveryOutcome.PermanentFailure($"SMTP server rejected the recipient ({statusCode}).")
            : DeliveryOutcome.TransientFailure($"SMTP server rejected the message ({exception.ErrorCode}, {statusCode}).");
    }

    private static MimeMessage CreateMessage(DeliveryRequest request, SmtpOptions settings)
    {
        var message = new MimeMessage
        {
            // Stored as the provider message id, so a delivered notification can be found in the mail server logs.
            MessageId = MimeUtils.GenerateMessageId(),
            Subject = request.Content.Subject ?? string.Empty,
            Body = new TextPart("plain") { Text = request.Content.Body },
        };

        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(request.Recipient.Address));
        message.Headers.Add(NotificationIdHeader, request.NotificationId.ToString());

        return message;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Notification {NotificationId}: SMTP server accepted the message: {Response}")]
    private static partial void LogSent(ILogger logger, NotificationId notificationId, string response);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification {NotificationId}: SMTP server rejected the message ({ErrorCode}, status {StatusCode})")]
    private static partial void LogRejected(ILogger logger, NotificationId notificationId, SmtpErrorCode errorCode, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification {NotificationId}: SMTP server unavailable")]
    private static partial void LogUnavailable(ILogger logger, Exception exception, NotificationId notificationId);
}
