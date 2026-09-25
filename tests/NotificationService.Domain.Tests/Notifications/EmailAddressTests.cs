using NotificationService.Domain.Common;
using NotificationService.Domain.Notifications;

namespace NotificationService.Domain.Tests.Notifications;

public class EmailAddressTests
{
    [Theory]
    [InlineData("jane@example.com")]
    [InlineData("jane.doe+orders@mail.example.co.uk")]
    public void Create_WithValidAddress_Succeeds(string value)
    {
        EmailAddress.Create(value).Value.ShouldBe(value);
    }

    [Fact]
    public void Create_TrimsWhitespace()
    {
        EmailAddress.Create("  jane@example.com ").Value.ShouldBe("jane@example.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("jane@")]
    [InlineData("Jane <jane@example.com>")]
    public void Create_WithInvalidAddress_Throws(string? value)
    {
        Should.Throw<DomainException>(() => EmailAddress.Create(value));
    }
}
