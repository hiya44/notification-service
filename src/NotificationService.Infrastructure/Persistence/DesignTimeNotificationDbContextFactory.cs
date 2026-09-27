using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NotificationService.Infrastructure.Persistence;

/// <summary>
/// Lets the dotnet-ef tool create the DbContext for generating migrations without starting the application.
/// The connection string is only used by commands that actually connect (e.g. "database update").
/// </summary>
public sealed class DesignTimeNotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    public NotificationDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=notifications;Username=notifications;Password=notifications")
            .Options);
}
