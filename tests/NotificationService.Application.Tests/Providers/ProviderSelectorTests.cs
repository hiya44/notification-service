using NotificationService.Application.Providers;
using NotificationService.Application.Tests.Fakes;
using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Tests.Providers;

public class ProviderSelectorTests
{
    private readonly FakeProvider _twilio = new("Twilio", Channel.Sms);
    private readonly FakeProvider _vonage = new("Vonage", Channel.Sms);
    private readonly FakeProvider _ses = new("AmazonSes", Channel.Email);

    [Fact]
    public void EligibleProvidersFor_ReturnsProvidersForChannel_OrderedByPriority()
    {
        var selector = CreateSelector(new()
        {
            ["Twilio"] = Configured(priority: 2, Channel.Sms),
            ["Vonage"] = Configured(priority: 1, Channel.Sms),
            ["AmazonSes"] = Configured(priority: 1, Channel.Email),
        });

        selector.EligibleProvidersFor(Channel.Sms).ShouldBe(new INotificationProvider[] { _vonage, _twilio });
        selector.EligibleProvidersFor(Channel.Email).ShouldBe(new INotificationProvider[] { _ses });
    }

    [Fact]
    public void EligibleProvidersFor_ExcludesDisabledProviders()
    {
        var selector = CreateSelector(new()
        {
            ["Twilio"] = Configured(priority: 1, Channel.Sms),
            ["Vonage"] = new ProviderOptions { Enabled = false, Priority = 2, Channels = [Channel.Sms] },
        });

        selector.EligibleProvidersFor(Channel.Sms).ShouldBe(new INotificationProvider[] { _twilio });
    }

    [Fact]
    public void EligibleProvidersFor_ExcludesProvidersNotConfiguredForChannel()
    {
        var selector = CreateSelector(new()
        {
            ["Twilio"] = Configured(priority: 1, Channel.Sms),
            ["Vonage"] = Configured(priority: 2),
        });

        selector.EligibleProvidersFor(Channel.Sms).ShouldBe(new INotificationProvider[] { _twilio });
    }

    [Fact]
    public void EligibleProvidersFor_ExcludesProvidersWithoutConfiguration()
    {
        var selector = CreateSelector(new()
        {
            ["Vonage"] = Configured(priority: 1, Channel.Sms),
        });

        selector.EligibleProvidersFor(Channel.Sms).ShouldBe(new INotificationProvider[] { _vonage });
    }

    [Fact]
    public void EligibleProvidersFor_ExcludesProvidersConfiguredForUnsupportedChannel()
    {
        var selector = CreateSelector(new()
        {
            ["Twilio"] = Configured(priority: 1, Channel.Sms, Channel.Email),
            ["AmazonSes"] = Configured(priority: 2, Channel.Email),
        });

        selector.EligibleProvidersFor(Channel.Email).ShouldBe(new INotificationProvider[] { _ses });
    }

    [Fact]
    public void EligibleProvidersFor_WithEqualPriority_OrdersByName()
    {
        var selector = CreateSelector(new()
        {
            ["Vonage"] = Configured(priority: 1, Channel.Sms),
            ["Twilio"] = Configured(priority: 1, Channel.Sms),
        });

        selector.EligibleProvidersFor(Channel.Sms).ShouldBe(new INotificationProvider[] { _twilio, _vonage });
    }

    [Fact]
    public void EligibleProvidersFor_WhenNoProviderIsEligible_ReturnsEmpty()
    {
        var selector = CreateSelector(new()
        {
            ["Twilio"] = new ProviderOptions { Enabled = false, Priority = 1, Channels = [Channel.Sms] },
        });

        selector.EligibleProvidersFor(Channel.Sms).ShouldBeEmpty();
    }

    [Fact]
    public void EligibleProvidersFor_MatchesProviderNamesCaseInsensitively()
    {
        var selector = CreateSelector(new()
        {
            ["twilio"] = Configured(priority: 1, Channel.Sms),
        });

        selector.EligibleProvidersFor(Channel.Sms).ShouldBe(new INotificationProvider[] { _twilio });
    }

    [Fact]
    public void EligibleProvidersFor_ReflectsConfigurationChanges()
    {
        var options = OptionsWith(new()
        {
            ["Twilio"] = Configured(priority: 1, Channel.Sms),
            ["Vonage"] = Configured(priority: 2, Channel.Sms),
        });
        var monitor = new TestOptionsMonitor<NotificationOptions>(options);
        var selector = new ProviderSelector([_twilio, _vonage, _ses], monitor);

        monitor.CurrentValue = OptionsWith(new()
        {
            ["Twilio"] = new ProviderOptions { Enabled = false, Priority = 1, Channels = [Channel.Sms] },
            ["Vonage"] = Configured(priority: 2, Channel.Sms),
        });

        selector.EligibleProvidersFor(Channel.Sms).ShouldBe(new INotificationProvider[] { _vonage });
    }

    private ProviderSelector CreateSelector(Dictionary<string, ProviderOptions> providers) =>
        new([_twilio, _vonage, _ses], new TestOptionsMonitor<NotificationOptions>(OptionsWith(providers)));

    private static NotificationOptions OptionsWith(Dictionary<string, ProviderOptions> providers)
    {
        var options = new NotificationOptions();
        foreach (var (name, settings) in providers)
        {
            options.Providers[name] = settings;
        }

        return options;
    }

    private static ProviderOptions Configured(int priority, params Channel[] channels) =>
        new() { Enabled = true, Priority = priority, Channels = channels.ToHashSet() };
}
