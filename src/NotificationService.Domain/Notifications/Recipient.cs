using NotificationService.Domain.Common;

namespace NotificationService.Domain.Notifications;

/// <summary>
/// Where a notification is delivered: a channel together with a channel-specific address
/// (a phone number for SMS, an email address for email).
/// The address is always validated for its channel, so a recipient can never be mismatched.
/// </summary>
public sealed record Recipient
{
    public Channel Channel { get; }

    public string Address { get; }

    private Recipient(Channel channel, string address)
    {
        Channel = channel;
        Address = address;
    }

    public static Recipient Sms(PhoneNumber phoneNumber) => new(Channel.Sms, phoneNumber.Value);

    public static Recipient Email(EmailAddress emailAddress) => new(Channel.Email, emailAddress.Value);

    public static Recipient Create(Channel channel, string? address) => channel switch
    {
        Channel.Sms => Sms(PhoneNumber.Create(address)),
        Channel.Email => Email(EmailAddress.Create(address)),
        _ => throw new DomainException($"Channel '{channel}' is not supported."),
    };
}
