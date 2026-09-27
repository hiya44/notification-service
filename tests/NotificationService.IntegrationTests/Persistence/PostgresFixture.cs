using Microsoft.EntityFrameworkCore;
using NotificationService.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace NotificationService.IntegrationTests.Persistence;

/// <summary>
/// Starts a disposable PostgreSQL container and applies the EF Core migrations to it.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    public NotificationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<NotificationDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task ResetAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE notifications, delivery_attempts",
            TestContext.Current.CancellationToken);
    }
}
