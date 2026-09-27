using NotificationService.Domain.Common;
using NotificationService.Domain.Notifications;

namespace NotificationService.Domain.Tests.Notifications;

public class IdempotencyKeyTests
{
    [Fact]
    public void Create_TrimsValue()
    {
        IdempotencyKey.Create("  order-42  ").Value.ShouldBe("order-42");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankValue_Throws(string? value)
    {
        Should.Throw<DomainException>(() => IdempotencyKey.Create(value));
    }

    [Fact]
    public void Create_WithTooLongValue_Throws()
    {
        Should.Throw<DomainException>(() => IdempotencyKey.Create(new string('k', IdempotencyKey.MaxLength + 1)));
    }
}
