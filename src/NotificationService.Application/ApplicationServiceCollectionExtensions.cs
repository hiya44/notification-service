using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotificationService.Application.Dispatching;
using NotificationService.Application.Notifications;
using NotificationService.Application.Providers;

namespace NotificationService.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the use cases and dispatching services. Options (<see cref="DispatchOptions"/>, <see cref="NotificationOptions"/>)
    /// are bound by the host, and an <see cref="Domain.Notifications.INotificationRepository"/> and providers must be registered.
    /// </summary>
    public static IServiceCollection AddNotificationApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);

        // Stateless apart from options monitors, so shared by all dispatches.
        services.AddSingleton<ProviderSelector>();
        services.AddSingleton<NotificationDispatcher>();

        // Scoped because they use the repository (one database context per request or per dispatched notification).
        services.AddScoped<SendNotificationHandler>();
        services.AddScoped<GetNotificationHandler>();
        services.AddScoped<DispatchNotificationHandler>();

        return services;
    }
}
