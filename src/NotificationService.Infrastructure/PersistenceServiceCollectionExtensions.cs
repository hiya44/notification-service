using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Domain.Notifications;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.Infrastructure;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<NotificationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<INotificationRepository, NotificationRepository>();

        return services;
    }
}
