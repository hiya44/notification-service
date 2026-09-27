using NotificationService.Domain.Notifications;

namespace NotificationService.Domain.Tests.Notifications;

public class RetryPolicyTests
{
    private static RetryPolicy WithoutJitter(int maxDispatches = 5) => new(
        maxDispatches,
        initialDelay: TimeSpan.FromMinutes(1),
        maxDelay: TimeSpan.FromMinutes(10),
        jitterFactor: 0);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    public void DelayAfter_GrowsExponentially(int failedDispatches, int expectedMinutes)
    {
        WithoutJitter().DelayAfter(failedDispatches).ShouldBe(TimeSpan.FromMinutes(expectedMinutes));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(20)]
    [InlineData(1_000)]
    public void DelayAfter_IsCappedAtMaxDelay(int failedDispatches)
    {
        WithoutJitter().DelayAfter(failedDispatches).ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Theory]
    [InlineData(0.0, 48)]
    [InlineData(0.5, 60)]
    [InlineData(0.99, 71.76)]
    public void DelayAfter_AppliesJitterWithinBounds(double randomValue, double expectedSeconds)
    {
        var policy = new RetryPolicy(
            maxDispatches: 5,
            initialDelay: TimeSpan.FromMinutes(1),
            maxDelay: TimeSpan.FromMinutes(10),
            jitterFactor: 0.2,
            randomSource: () => randomValue);

        policy.DelayAfter(1).TotalSeconds.ShouldBe(expectedSeconds, tolerance: 0.001);
    }

    [Fact]
    public void DelayAfter_WithJitter_NeverExceedsMaxDelay()
    {
        var policy = new RetryPolicy(
            maxDispatches: 5,
            initialDelay: TimeSpan.FromMinutes(1),
            maxDelay: TimeSpan.FromMinutes(10),
            jitterFactor: 0.5,
            randomSource: () => 0.99);

        policy.DelayAfter(10).ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public void AllowsRetryAfter_IsLimitedByMaxDispatches(int failedDispatches, bool expected)
    {
        WithoutJitter(maxDispatches: 3).AllowsRetryAfter(failedDispatches).ShouldBe(expected);
    }

    [Fact]
    public void DelayAfter_WithoutFailedDispatches_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => WithoutJitter().DelayAfter(0));
    }

    [Fact]
    public void Constructor_WithInvalidSettings_Throws()
    {
        var minute = TimeSpan.FromMinutes(1);

        Should.Throw<ArgumentOutOfRangeException>(() => new RetryPolicy(0, minute, minute));
        Should.Throw<ArgumentOutOfRangeException>(() => new RetryPolicy(1, TimeSpan.Zero, minute));
        Should.Throw<ArgumentOutOfRangeException>(() => new RetryPolicy(1, minute, TimeSpan.FromSeconds(1)));
        Should.Throw<ArgumentOutOfRangeException>(() => new RetryPolicy(1, minute, minute, jitterFactor: 1));
        Should.Throw<ArgumentOutOfRangeException>(() => new RetryPolicy(1, minute, minute, jitterFactor: -0.1));
    }
}
