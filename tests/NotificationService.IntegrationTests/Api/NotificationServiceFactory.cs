using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using NotificationService.Application.Providers;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Fakes;
using NotificationService.IntegrationTests.Persistence;

namespace NotificationService.IntegrationTests.Api;

/// <summary>
/// Runs the real application (endpoints, dispatch worker, EF Core) against a disposable PostgreSQL container.
/// Only two things are substituted: the providers, by scriptable fakes with the real names so the real provider
/// configuration and its validation apply, and the clock, so tests move time to trigger polling and retries.
/// </summary>
public sealed class NotificationServiceFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMinutes(1);
    public const int MaxDispatches = 3;

    private readonly PostgresFixture _postgres = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

    public TestProvider Twilio { get; } = new("Twilio", Channel.Sms);

    public TestProvider Vonage { get; } = new("Vonage", Channel.Sms);

    public TestProvider Smtp { get; } = new("Smtp", Channel.Email);

    public TestProvider AmazonSes { get; } = new("AmazonSes", Channel.Email);

    public string DatabaseConnectionString => _postgres.ConnectionString;

    private IEnumerable<TestProvider> Providers => [Twilio, Vonage, Smtp, AmazonSes];

    public async ValueTask InitializeAsync() => await _postgres.InitializeAsync();

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>Empties the database and makes every provider deliver again. The clock keeps moving forward.</summary>
    public async Task ResetAsync()
    {
        await _postgres.ResetAsync();
        foreach (var provider in Providers)
        {
            provider.Reset();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development: keeps the demo settings (simulated failures, short retries) out of the tests.
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:Notifications", _postgres.ConnectionString);
        builder.UseSetting("Notifications:Worker:PollingInterval", PollingInterval.ToString());
        builder.UseSetting("Notifications:Dispatch:Retry:InitialDelay", InitialRetryDelay.ToString());
        builder.UseSetting("Notifications:Dispatch:Retry:MaxDispatches", MaxDispatches.ToString(CultureInfo.InvariantCulture));
        builder.UseSetting("Notifications:Dispatch:Retry:JitterFactor", "0");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationProvider>();
            foreach (var provider in Providers)
            {
                services.AddSingleton<INotificationProvider>(provider);
            }

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<NotificationServiceFactory>
{
    /// <summary>Tests sharing the application run one at a time, since they share its clock and providers.</summary>
    public const string Name = "Api";
}
