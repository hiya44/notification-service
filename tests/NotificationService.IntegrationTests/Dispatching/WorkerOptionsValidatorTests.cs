using NotificationService.Api.Dispatching;
using NotificationService.Application.Dispatching;
using NotificationService.Application.Providers;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Fakes;

namespace NotificationService.IntegrationTests.Dispatching;

public class WorkerOptionsValidatorTests
{
    // Two enabled SMS providers with a 10 second timeout: a dispatch can take up to 20 seconds.
    private readonly WorkerOptionsValidator _validator = new(
        new TestOptionsMonitor<DispatchOptions>(new DispatchOptions { DeliveryAttemptTimeout = TimeSpan.FromSeconds(10) }),
        new TestOptionsMonitor<NotificationOptions>(new NotificationOptions
        {
            Providers =
            {
                ["Twilio"] = new ProviderOptions { Priority = 1, Channels = [Channel.Sms] },
                ["Vonage"] = new ProviderOptions { Priority = 2, Channels = [Channel.Sms] },
                ["AmazonSes"] = new ProviderOptions { Priority = 1, Channels = [Channel.Email] },
                ["Smtp"] = new ProviderOptions { Enabled = false, Priority = 2, Channels = [Channel.Email, Channel.Sms] },
            },
        }));

    [Fact]
    public void Validate_WithDefaults_Succeeds()
    {
        _validator.Validate(null, new WorkerOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithLeaseLongerThanLongestDispatch_Succeeds()
    {
        _validator.Validate(null, new WorkerOptions { LeaseDuration = TimeSpan.FromSeconds(21) }).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(20)]
    [InlineData(10)]
    public void Validate_WithLeaseNotLongerThanLongestDispatch_Fails(int leaseSeconds)
    {
        var result = _validator.Validate(null, new WorkerOptions { LeaseDuration = TimeSpan.FromSeconds(leaseSeconds) });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("LeaseDuration");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(WorkerOptionsValidator.MaxBatchSize + 1)]
    public void Validate_WithBatchSizeOutOfRange_Fails(int batchSize)
    {
        var result = _validator.Validate(null, new WorkerOptions { BatchSize = batchSize });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("BatchSize");
    }

    [Fact]
    public void Validate_WithNonPositivePollingInterval_Fails()
    {
        var result = _validator.Validate(null, new WorkerOptions { PollingInterval = TimeSpan.Zero });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("PollingInterval");
    }
}
