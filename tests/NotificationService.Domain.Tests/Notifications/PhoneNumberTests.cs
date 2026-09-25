using NotificationService.Domain.Common;
using NotificationService.Domain.Notifications;

namespace NotificationService.Domain.Tests.Notifications;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("+37060012345", "+37060012345")]
    [InlineData("+370 600 12345", "+37060012345")]
    [InlineData("+1 (415) 555-0100", "+14155550100")]
    public void Create_WithValidNumber_NormalizesToE164(string value, string expected)
    {
        PhoneNumber.Create(value).Value.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("860012345")]
    [InlineData("+0123456789")]
    [InlineData("+12345")]
    [InlineData("+1234567890123456")]
    [InlineData("+3706001234a")]
    public void Create_WithInvalidNumber_Throws(string? value)
    {
        Should.Throw<DomainException>(() => PhoneNumber.Create(value));
    }
}
