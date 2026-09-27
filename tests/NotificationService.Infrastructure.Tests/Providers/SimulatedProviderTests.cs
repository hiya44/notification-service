using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NotificationService.Application.Providers;
using NotificationService.Domain.Notifications;
using NotificationService.Infrastructure.Providers.Simulated;
using NotificationService.Infrastructure.Tests.Fakes;

namespace NotificationService.Infrastructure.Tests.Providers;

public class SimulatedProviderTests
{
    private static readonly DeliveryRequest SmsRequest = new(
        NotificationId.New(),
        Recipient.Sms(PhoneNumber.Create("+37060012345")),
        NotificationContent.Create(null, "Your code is 123456"));

    private readonly FakeTimeProvider _timeProvider = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SendAsync_InNormalModeWithoutFailureRate_Delivers()
    {
        var provider = CreateProvider(new SimulatedProviderOptions { Latency = TimeSpan.Zero });

        var outcome = await provider.SendAsync(SmsRequest, CancellationToken);

        outcome.Kind.ShouldBe(DeliveryOutcomeKind.Delivered);
        outcome.ProviderMessageId.ShouldNotBeNull().ShouldStartWith("testsms-");
    }

    [Theory]
    [InlineData(0.29, DeliveryOutcomeKind.TransientFailure)]
    [InlineData(0.30, DeliveryOutcomeKind.Delivered)]
    [InlineData(0.99, DeliveryOutcomeKind.Delivered)]
    public async Task SendAsync_InNormalMode_FailsTransientlyAtFailureRate(double randomValue, DeliveryOutcomeKind expected)
    {
        var provider = CreateProvider(
            new SimulatedProviderOptions { FailureRate = 0.3, Latency = TimeSpan.Zero },
            new FixedRandom(randomValue));

        var outcome = await provider.SendAsync(SmsRequest, CancellationToken);

        outcome.Kind.ShouldBe(expected);
    }

    [Fact]
    public async Task SendAsync_InAlwaysFailMode_FailsTransiently()
    {
        var provider = CreateProvider(new SimulatedProviderOptions { Mode = SimulationMode.AlwaysFail, Latency = TimeSpan.Zero });

        var outcome = await provider.SendAsync(SmsRequest, CancellationToken);

        outcome.Kind.ShouldBe(DeliveryOutcomeKind.TransientFailure);
        outcome.FailureReason.ShouldBe("Simulated TestSms outage.");
    }

    [Fact]
    public async Task SendAsync_InPermanentFailureMode_FailsPermanently()
    {
        var provider = CreateProvider(new SimulatedProviderOptions { Mode = SimulationMode.PermanentFailure, Latency = TimeSpan.Zero });

        var outcome = await provider.SendAsync(SmsRequest, CancellationToken);

        outcome.Kind.ShouldBe(DeliveryOutcomeKind.PermanentFailure);
    }

    [Fact]
    public async Task SendAsync_WithLatency_CompletesOnlyAfterLatency()
    {
        var provider = CreateProvider(new SimulatedProviderOptions { Latency = TimeSpan.FromSeconds(2) });

        var sending = provider.SendAsync(SmsRequest, CancellationToken);
        _timeProvider.Advance(TimeSpan.FromSeconds(1.9));
        sending.IsCompleted.ShouldBeFalse();

        _timeProvider.Advance(TimeSpan.FromSeconds(0.1));
        (await sending).Kind.ShouldBe(DeliveryOutcomeKind.Delivered);
    }

    [Fact]
    public async Task SendAsync_WhenCancelledDuringLatency_StopsWaiting()
    {
        var provider = CreateProvider(new SimulatedProviderOptions { Latency = TimeSpan.FromMinutes(5) });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

        var sending = provider.SendAsync(SmsRequest, cancellation.Token);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(sending);
    }

    [Fact]
    public async Task SendAsync_ReadsSettingsOnEveryAttempt()
    {
        var options = new TestOptionsMonitor<SimulatedProviderOptions>(new() { Mode = SimulationMode.AlwaysFail, Latency = TimeSpan.Zero });
        var provider = new TestSmsProvider(options, _timeProvider);

        (await provider.SendAsync(SmsRequest, CancellationToken)).Kind.ShouldBe(DeliveryOutcomeKind.TransientFailure);

        options.CurrentValue = new SimulatedProviderOptions { Latency = TimeSpan.Zero };

        (await provider.SendAsync(SmsRequest, CancellationToken)).Kind.ShouldBe(DeliveryOutcomeKind.Delivered);
    }

    [Fact]
    public async Task SendAsync_WithUnsupportedChannel_Throws()
    {
        var provider = CreateProvider(new SimulatedProviderOptions { Latency = TimeSpan.Zero });
        var emailRequest = new DeliveryRequest(
            NotificationId.New(),
            Recipient.Email(EmailAddress.Create("jane@example.com")),
            NotificationContent.Create("Subject", "Body"));

        await Should.ThrowAsync<ArgumentException>(() => provider.SendAsync(emailRequest, CancellationToken));
    }

    private TestSmsProvider CreateProvider(SimulatedProviderOptions options, Random? random = null) =>
        new(new TestOptionsMonitor<SimulatedProviderOptions>(options), _timeProvider, random);

    private sealed class TestSmsProvider(
        TestOptionsMonitor<SimulatedProviderOptions> options,
        TimeProvider timeProvider,
        Random? random = null)
        : SimulatedProvider("TestSms", new HashSet<Channel> { Channel.Sms }, options, timeProvider, NullLogger.Instance, random);

    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
    }
}
