using Microsoft.Extensions.Options;
using NotificationService.Application.Providers;
using NotificationService.Application.Tests.Fakes;
using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Tests.Providers;

public class NotificationOptionsValidatorTests
{
    private readonly NotificationOptionsValidator _validator = new(
    [
        new FakeProvider("Twilio", Channel.Sms),
        new FakeProvider("AmazonSes", Channel.Email),
    ]);

    [Fact]
    public void Validate_WithValidConfiguration_Succeeds()
    {
        var result = Validate(new()
        {
            ["Twilio"] = new ProviderOptions { Priority = 1, Channels = [Channel.Sms] },
            ["AmazonSes"] = new ProviderOptions { Priority = 1, Channels = [Channel.Email] },
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithNoProvidersConfigured_Succeeds()
    {
        Validate([]).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithProviderNameInDifferentCase_Succeeds()
    {
        var result = Validate(new()
        {
            ["twilio"] = new ProviderOptions { Priority = 1, Channels = [Channel.Sms] },
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithUnknownProvider_Fails()
    {
        var result = Validate(new()
        {
            ["Twillio"] = new ProviderOptions { Priority = 1, Channels = [Channel.Sms] },
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("'Twillio' is configured but does not exist");
    }

    [Fact]
    public void Validate_WithUnsupportedChannel_Fails()
    {
        var result = Validate(new()
        {
            ["Twilio"] = new ProviderOptions { Priority = 1, Channels = [Channel.Sms, Channel.Email] },
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("'Twilio' is configured for Email, but only supports Sms");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithPriorityBelowOne_Fails(int priority)
    {
        var result = Validate(new()
        {
            ["Twilio"] = new ProviderOptions { Priority = priority, Channels = [Channel.Sms] },
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain($"priority {priority}");
    }

    [Fact]
    public void Validate_WithEnabledProviderWithoutChannels_Fails()
    {
        var result = Validate(new()
        {
            ["Twilio"] = new ProviderOptions { Priority = 1, Channels = [] },
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("not configured for any channel");
    }

    [Fact]
    public void Validate_WithDisabledProviderWithoutChannels_Succeeds()
    {
        var result = Validate(new()
        {
            ["Twilio"] = new ProviderOptions { Enabled = false, Priority = 1, Channels = [] },
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_WithSeveralProblems_ReportsAll()
    {
        var result = Validate(new()
        {
            ["Unknown"] = new ProviderOptions { Priority = 1, Channels = [Channel.Sms] },
            ["AmazonSes"] = new ProviderOptions { Priority = 0, Channels = [Channel.Sms] },
        });

        result.Failures.ShouldNotBeNull();
        result.Failures.Count().ShouldBe(3);
    }

    private ValidateOptionsResult Validate(Dictionary<string, ProviderOptions> providers) =>
        _validator.Validate(null, new NotificationOptions
        {
            Providers = new Dictionary<string, ProviderOptions>(providers, StringComparer.OrdinalIgnoreCase),
        });
}
