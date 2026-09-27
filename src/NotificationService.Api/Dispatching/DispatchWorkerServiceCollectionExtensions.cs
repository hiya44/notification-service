using Microsoft.Extensions.Options;
using NotificationService.Application.Dispatching;

namespace NotificationService.Api.Dispatching;

public static class DispatchWorkerServiceCollectionExtensions
{
    /// <summary>Binds and validates the dispatch and worker settings and registers the background dispatch worker.</summary>
    public static IServiceCollection AddDispatchWorker(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DispatchOptions>()
            .Bind(configuration.GetSection(DispatchOptions.SectionName))
            .Validate(dispatch => dispatch.DeliveryAttemptTimeout > TimeSpan.Zero, "Notifications:Dispatch:DeliveryAttemptTimeout must be positive.")
            .Validate(dispatch => dispatch.Retry.MaxDispatches >= 1, "Notifications:Dispatch:Retry:MaxDispatches must be at least 1.")
            .Validate(dispatch => dispatch.Retry.InitialDelay > TimeSpan.Zero, "Notifications:Dispatch:Retry:InitialDelay must be positive.")
            .Validate(dispatch => dispatch.Retry.MaxDelay >= dispatch.Retry.InitialDelay, "Notifications:Dispatch:Retry:MaxDelay must not be shorter than InitialDelay.")
            .Validate(dispatch => dispatch.Retry.JitterFactor is >= 0 and < 1, "Notifications:Dispatch:Retry:JitterFactor must be at least 0 and less than 1.")
            .ValidateOnStart();

        services.AddOptions<WorkerOptions>()
            .Bind(configuration.GetSection(WorkerOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<WorkerOptions>, WorkerOptionsValidator>();

        // Registered as itself too, so tests can drive a single poll without starting the host.
        services.AddSingleton<DispatchWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<DispatchWorker>());

        return services;
    }
}
