using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NotificationService.Application.Providers;
using NotificationService.Infrastructure.Providers.Simulated;
using NotificationService.Infrastructure.Providers.Smtp;

namespace NotificationService.Infrastructure;

public static class ProviderServiceCollectionExtensions
{
    /// <summary>
    /// Registers all provider implementations and binds their configuration. Which providers are actually used,
    /// and in which order, is decided by the "Notifications:Providers" section; it is validated at startup
    /// so a typo in a provider name or channel stops the application instead of silently dropping traffic.
    /// </summary>
    public static IServiceCollection AddNotificationProviders(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection(NotificationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<NotificationOptions>, NotificationOptionsValidator>();

        AddSimulatedProvider<TwilioProvider>(services, configuration, TwilioProvider.ProviderName);
        AddSimulatedProvider<VonageProvider>(services, configuration, VonageProvider.ProviderName);
        AddSimulatedProvider<AmazonSesProvider>(services, configuration, AmazonSesProvider.ProviderName);

        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName))
            .Validate(smtp => !string.IsNullOrWhiteSpace(smtp.Host), "Notifications:Smtp:Host is required.")
            .Validate(smtp => smtp.Port is > 0 and <= 65535, "Notifications:Smtp:Port must be between 1 and 65535.")
            .Validate(smtp => MailAddress.TryCreate(smtp.FromAddress, out _), "Notifications:Smtp:FromAddress must be a valid email address.")
            .ValidateOnStart();
        services.AddSingleton<INotificationProvider, SmtpEmailProvider>();

        return services;
    }

    private static void AddSimulatedProvider<TProvider>(IServiceCollection services, IConfiguration configuration, string name)
        where TProvider : SimulatedProvider
    {
        // Simulation settings sit next to the provider's routing settings, e.g. "Notifications:Providers:Twilio:Simulation".
        var section = configuration.GetSection($"{NotificationOptions.SectionName}:Providers:{name}:{SimulatedProviderOptions.SectionName}");

        services.AddOptions<SimulatedProviderOptions>(name)
            .Bind(section)
            .Validate(simulation => simulation.FailureRate is >= 0 and <= 1, $"{section.Path}:FailureRate must be between 0 and 1.")
            .Validate(simulation => simulation.Latency >= TimeSpan.Zero, $"{section.Path}:Latency must not be negative.")
            .ValidateOnStart();

        services.AddSingleton<INotificationProvider, TProvider>();
    }
}
