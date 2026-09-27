using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NotificationService.Api.Dispatching;
using NotificationService.Application;
using NotificationService.Application.Providers;
using NotificationService.Domain.Notifications;
using NotificationService.Infrastructure;
using NotificationService.Infrastructure.Persistence;
using NotificationService.IntegrationTests.Fakes;
using NotificationService.IntegrationTests.Persistence;

namespace NotificationService.IntegrationTests.Dispatching;

public sealed class DispatchWorkerTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset Now = TestNotifications.Now;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly TestProvider _provider = new("Test", Channel.Sms);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await postgres.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task ProcessDueNotifications_DispatchesAndSavesEachClaimedNotification()
    {
        var notifications = await AddAsync(TestNotifications.Sms(), TestNotifications.Sms(), TestNotifications.Sms());
        await using var services = BuildServices();

        var claimed = await Worker(services).ProcessDueNotificationsAsync(CancellationToken);

        claimed.ShouldBe(3);
        foreach (var notification in notifications)
        {
            var saved = await LoadAsync(notification.Id);
            saved.Status.ShouldBe(NotificationStatus.Delivered);
            saved.DeliveryAttempts.ShouldHaveSingleItem().ProviderName.ShouldBe("Test");
        }
    }

    [Fact]
    public async Task ProcessDueNotifications_WhenAllProvidersFail_SavesRetryAndReleasesLease()
    {
        _provider.Behavior = (_, _) => Task.FromResult(DeliveryOutcome.TransientFailure("Service unavailable"));
        var notification = (await AddAsync(TestNotifications.Sms())).Single();
        await using var services = BuildServices();

        await Worker(services).ProcessDueNotificationsAsync(CancellationToken);

        var saved = await LoadAsync(notification.Id);
        saved.Status.ShouldBe(NotificationStatus.Pending);
        saved.FailedDispatchCount.ShouldBe(1);
        saved.NextAttemptAt.ShouldBe(Now.AddMinutes(1));

        // Claimable again when the retry is due, although the lease would only expire later.
        _time.SetUtcNow(Now.AddMinutes(1));
        (await Worker(services).ProcessDueNotificationsAsync(CancellationToken)).ShouldBe(1);
        _provider.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task ProcessDueNotifications_WhenOneNotificationFails_DispatchesTheOthers()
    {
        var notifications = await AddAsync(TestNotifications.Sms(), TestNotifications.Sms(), TestNotifications.Sms());
        var broken = notifications[1];
        await using var services = BuildServices(configure: services => services.AddScoped<INotificationRepository>(provider =>
            new FailingRepository(new NotificationRepository(provider.GetRequiredService<NotificationDbContext>()), broken.Id)));

        var claimed = await Worker(services).ProcessDueNotificationsAsync(CancellationToken);

        claimed.ShouldBe(3);
        (await LoadAsync(notifications[0].Id)).Status.ShouldBe(NotificationStatus.Delivered);
        (await LoadAsync(notifications[2].Id)).Status.ShouldBe(NotificationStatus.Delivered);
        (await LoadAsync(broken.Id)).Status.ShouldBe(NotificationStatus.Pending);
    }

    [Fact]
    public async Task ProcessDueNotifications_WhenOneNotificationFails_RetriesItAfterLeaseExpires()
    {
        var broken = (await AddAsync(TestNotifications.Sms())).Single();
        await using (var failing = BuildServices(configure: services => services.AddScoped<INotificationRepository>(provider =>
            new FailingRepository(new NotificationRepository(provider.GetRequiredService<NotificationDbContext>()), broken.Id))))
        {
            await Worker(failing).ProcessDueNotificationsAsync(CancellationToken);
        }

        await using var services = BuildServices();

        (await Worker(services).ProcessDueNotificationsAsync(CancellationToken)).ShouldBe(0);

        _time.Advance(Lease);
        (await Worker(services).ProcessDueNotificationsAsync(CancellationToken)).ShouldBe(1);
        (await LoadAsync(broken.Id)).Status.ShouldBe(NotificationStatus.Delivered);
    }

    [Fact]
    public async Task ProcessDueNotifications_ClaimsAtMostBatchSize()
    {
        await AddAsync(TestNotifications.Sms(), TestNotifications.Sms(), TestNotifications.Sms());
        await using var services = BuildServices(batchSize: 2);
        var worker = Worker(services);

        (await worker.ProcessDueNotificationsAsync(CancellationToken)).ShouldBe(2);
        (await worker.ProcessDueNotificationsAsync(CancellationToken)).ShouldBe(1);
        (await worker.ProcessDueNotificationsAsync(CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task ProcessDueNotifications_DispatchesBatchConcurrently()
    {
        // Each call waits until all three have started: this only completes if they run at the same time.
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _provider.Behavior = async (_, cancellationToken) =>
        {
            if (_provider.CallCount == 3)
            {
                allStarted.TrySetResult();
            }

            await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return DeliveryOutcome.Delivered();
        };
        var notifications = await AddAsync(TestNotifications.Sms(), TestNotifications.Sms(), TestNotifications.Sms());
        await using var services = BuildServices();

        await Worker(services).ProcessDueNotificationsAsync(CancellationToken);

        foreach (var notification in notifications)
        {
            (await LoadAsync(notification.Id)).Status.ShouldBe(NotificationStatus.Delivered);
        }
    }

    [Fact]
    public async Task ExecuteAsync_DrainsBacklogWithoutWaiting_ThenPollsOnEachTick()
    {
        var backlog = await AddAsync(TestNotifications.Sms(), TestNotifications.Sms(), TestNotifications.Sms());
        await using var services = BuildServices(batchSize: 1);
        var worker = Worker(services);

        await worker.StartAsync(CancellationToken);
        try
        {
            // Batches of one, and the clock does not move: all three are only delivered if full batches skip the wait.
            await EventuallyAsync(async () => (await LoadAllAsync(backlog)).All(n => n.Status == NotificationStatus.Delivered));

            var later = (await AddAsync(TestNotifications.Sms(createdAt: Now))).Single();
            _time.Advance(PollingInterval);

            await EventuallyAsync(async () => (await LoadAsync(later.Id)).Status == NotificationStatus.Delivered);
        }
        finally
        {
            await worker.StopAsync(CancellationToken);
        }
    }

    private ServiceProvider BuildServices(int batchSize = 10, Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notifications:Worker:BatchSize"] = batchSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Notifications:Worker:PollingInterval"] = PollingInterval.ToString(),
                ["Notifications:Worker:LeaseDuration"] = Lease.ToString(),
                ["Notifications:Dispatch:Retry:JitterFactor"] = "0",
            })
            .Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(_time)
            .AddNotificationApplication()
            .AddNotificationPersistence(postgres.ConnectionString)
            .AddDispatchWorker(configuration)
            .AddSingleton<INotificationProvider>(_provider)
            .Configure<NotificationOptions>(options =>
                options.Providers["Test"] = new ProviderOptions { Priority = 1, Channels = [Channel.Sms] });

        configure?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static DispatchWorker Worker(IServiceProvider services) => services.GetRequiredService<DispatchWorker>();

    private async Task<Notification[]> AddAsync(params Notification[] notifications)
    {
        await using var dbContext = postgres.CreateDbContext();
        var repository = new NotificationRepository(dbContext);
        foreach (var notification in notifications)
        {
            (await repository.TryAddAsync(notification, CancellationToken)).ShouldBeTrue();
        }

        return notifications;
    }

    private async Task<Notification> LoadAsync(NotificationId id)
    {
        await using var dbContext = postgres.CreateDbContext();
        return (await new NotificationRepository(dbContext).GetAsync(id, CancellationToken)).ShouldNotBeNull();
    }

    private async Task<IReadOnlyList<Notification>> LoadAllAsync(IEnumerable<Notification> notifications)
    {
        var loaded = new List<Notification>();
        foreach (var notification in notifications)
        {
            loaded.Add(await LoadAsync(notification.Id));
        }

        return loaded;
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met within 10 seconds.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken);
        }
    }

    /// <summary>Fails to load one notification, simulating an unexpected error while processing it.</summary>
    private sealed class FailingRepository(INotificationRepository inner, NotificationId failingId) : INotificationRepository
    {
        public Task<Notification?> GetAsync(NotificationId id, CancellationToken cancellationToken) =>
            id == failingId ? throw new InvalidOperationException("Simulated failure.") : inner.GetAsync(id, cancellationToken);

        public Task<bool> TryAddAsync(Notification notification, CancellationToken cancellationToken) =>
            inner.TryAddAsync(notification, cancellationToken);

        public Task<Notification?> FindByIdempotencyKeyAsync(IdempotencyKey idempotencyKey, CancellationToken cancellationToken) =>
            inner.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);

        public Task<IReadOnlyList<NotificationId>> ClaimDueAsync(DateTimeOffset now, int maxCount, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
            inner.ClaimDueAsync(now, maxCount, leaseDuration, cancellationToken);

        public Task UpdateAsync(Notification notification, CancellationToken cancellationToken) =>
            inner.UpdateAsync(notification, cancellationToken);
    }
}
