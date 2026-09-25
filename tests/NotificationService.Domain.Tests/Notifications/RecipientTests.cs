using NotificationService.Domain.Common;
using NotificationService.Domain.Notifications;

namespace NotificationService.Domain.Tests.Notifications;

public class RecipientTests
{
    [Fact]
    public void Create_ForSms_WithPhoneNumber_Succeeds()
    {
        var recipient = Recipient.Create(Channel.Sms, "+37060012345");

        recipient.Channel.ShouldBe(Channel.Sms);
        recipient.Address.ShouldBe("+37060012345");
    }

    [Fact]
    public void Create_ForEmail_WithEmailAddress_Succeeds()
    {
        var recipient = Recipient.Create(Channel.Email, "jane@example.com");

        recipient.Channel.ShouldBe(Channel.Email);
        recipient.Address.ShouldBe("jane@example.com");
    }

    [Theory]
    [InlineData(Channel.Sms, "jane@example.com")]
    [InlineData(Channel.Email, "+37060012345")]
    public void Create_WithAddressNotMatchingChannel_Throws(Channel channel, string address)
    {
        Should.Throw<DomainException>(() => Recipient.Create(channel, address));
    }

    [Fact]
    public void Create_WithUnknownChannel_Throws()
    {
        Should.Throw<DomainException>(() => Recipient.Create((Channel)99, "jane@example.com"));
    }

    [Fact]
    public void Recipients_WithSameChannelAndAddress_AreEqual()
    {
        Recipient.Create(Channel.Sms, "+370 600 12345")
            .ShouldBe(Recipient.Create(Channel.Sms, "+37060012345"));
    }
}
