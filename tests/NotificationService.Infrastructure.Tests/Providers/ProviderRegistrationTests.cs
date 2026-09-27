using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NotificationService.Application.Providers;
using NotificationService.Domain.Notifications;

namespace NotificationService.Infrastructure.Tests.Providers;

public class ProviderRegistrationTests
{
    private static readonly Dictionary<string, string?> ValidConfiguration = new()
    {
        ["Notifications:Providers:Twilio:Priority"] = "1",
        ["Notifications:Providers:Twilio:Channels:0"] = "Sms",
        ["Notifications:Providers:Twilio:Simulation:Latency"] = "00:00:00",
        ["Notifications:Providers:Vonage:Priority"] = "2",
        ["Notifications:Providers:Vonage:Channels:0"] = "Sms",
        ["Notifications:Providers:Vonage:Simulation:Latency"] = "00:00:00",
        ["Notifications:Providers:Smtp:Priority"] = "1",
        ["Notifications:Providers:Smtp:Channels:0"] = "Email",
    };

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void AddNotificationProviders_RegistersAllProviders()
    {
        using var services = BuildServices(ValidConfiguration);

        services.GetServices<INotificationProvider>()
            .Select(provider => provider.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBe(["AmazonSes", "Smtp", "Twilio", "Vonage"]);
    }

    [Fact]
    public void StartupValidation_WithValidConfiguration_Succeeds()
    {
        using var services = BuildServices(ValidConfiguration);

        Should.NotThrow(() => services.GetRequiredService<IStartupValidator>().Validate());
    }

    [Fact]
    public void StartupValidation_WithUnknownProvider_Fails()
    {
        using var services = BuildServices(With(("Notifications:Providers:Twillio:Priority", "1")));

        var exception = Should.Throw<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
        exception.Message.ShouldContain("'Twillio' is configured but does not exist");
    }

    [Fact]
    public void StartupValidation_WithUnsupportedChannel_Fails()
    {
        using var services = BuildServices(With(("Notifications:Providers:Smtp:Channels:1", "Sms")));

        var exception = Should.Throw<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
        exception.Message.ShouldContain("'Smtp' is configured for Sms");
    }

    [Fact]
    public void StartupValidation_WithInvalidFailureRate_Fails()
    {
        using var services = BuildServices(With(("Notifications:Providers:Twilio:Simulation:FailureRate", "1.5")));

        var exception = Should.Throw<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
        exception.Message.ShouldContain("Twilio:Simulation:FailureRate must be between 0 and 1");
    }

    [Fact]
    public void StartupValidation_WithInvalidSmtpSettings_Fails()
    {
        using var services = BuildServices(With(("Notifications:Smtp:Port", "0"), ("Notifications:Smtp:FromAddress", "not an address")));

        var exception = Should.Throw<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
        exception.Message.ShouldContain("Port must be between 1 and 65535");
        exception.Message.ShouldContain("FromAddress must be a valid email address");
    }

    [Fact]
    public void Binding_ProviderNames_AreCaseInsensitive()
    {
        using var services = BuildServices(new Dictionary<string, string?>
        {
            ["Notifications:Providers:twilio:Priority"] = "1",
            ["Notifications:Providers:twilio:Channels:0"] = "Sms",
        });

        var selector = new ProviderSelector(
            services.GetServices<INotificationProvider>(),
            services.GetRequiredService<IOptionsMonitor<NotificationOptions>>());

        selector.EligibleProvidersFor(Channel.Sms).Select(provider => provider.Name).ShouldBe(["Twilio"]);
    }

    [Fact]
    public async Task Binding_SimulationSettings_ApplyOnlyToTheirProvider()
    {
        using var services = BuildServices(With(("Notifications:Providers:Twilio:Simulation:Mode", "AlwaysFail")));
        var providers = services.GetServices<INotificationProvider>().ToDictionary(provider => provider.Name);
        var request = new DeliveryRequest(
            NotificationId.New(),
            Recipient.Sms(PhoneNumber.Create("+37060012345")),
            NotificationContent.Create(null, "Your code is 123456"));

        (await providers["Twilio"].SendAsync(request, CancellationToken)).Kind.ShouldBe(DeliveryOutcomeKind.TransientFailure);
        (await providers["Vonage"].SendAsync(request, CancellationToken)).Kind.ShouldBe(DeliveryOutcomeKind.Delivered);
    }

    private static Dictionary<string, string?> With(params (string Key, string Value)[] overrides)
    {
        var configuration = new Dictionary<string, string?>(ValidConfiguration);
        foreach (var (key, value) in overrides)
        {
            configuration[key] = value;
        }

        return configuration;
    }

    private static ServiceProvider BuildServices(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new ServiceCollection()
            .AddLogging()
            .AddNotificationProviders(configuration)
            .BuildServiceProvider();
    }
}
