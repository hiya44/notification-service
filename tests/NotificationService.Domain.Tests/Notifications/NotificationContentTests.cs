using NotificationService.Domain.Common;
using NotificationService.Domain.Notifications;

namespace NotificationService.Domain.Tests.Notifications;

public class NotificationContentTests
{
    [Fact]
    public void Create_WithSubjectAndBody_Succeeds()
    {
        var content = NotificationContent.Create(" Hello ", "Body");

        content.Subject.ShouldBe("Hello");
        content.Body.ShouldBe("Body");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_WithBlankSubject_HasNoSubject(string? subject)
    {
        NotificationContent.Create(subject, "Body").Subject.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutBody_Throws(string? body)
    {
        Should.Throw<DomainException>(() => NotificationContent.Create("Subject", body));
    }

    [Fact]
    public void Create_WithTooLongBody_Throws()
    {
        var body = new string('a', NotificationContent.MaxBodyLength + 1);

        Should.Throw<DomainException>(() => NotificationContent.Create(null, body));
    }

    [Fact]
    public void Create_WithTooLongSubject_Throws()
    {
        var subject = new string('a', NotificationContent.MaxSubjectLength + 1);

        Should.Throw<DomainException>(() => NotificationContent.Create(subject, "Body"));
    }
}
