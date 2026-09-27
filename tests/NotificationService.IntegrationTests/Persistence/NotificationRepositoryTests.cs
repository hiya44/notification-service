using NotificationService.Domain.Common;
using NotificationService.Domain.Notifications;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.IntegrationTests.Persistence;

public sealed class NotificationRepositoryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTimeOffset Now = TestNotifications.Now;
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await postgres.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task TryAdd_ThenGet_RoundTripsAllState()
    {
        var notification = TestNotifications.Email(idempotencyKey: "order-42");
        notification.RecordDeliveryAttempt("AmazonSes", DeliveryOutcome.TransientFailure("Throttled"), Now);
        await AddAsync(notification);

        var loaded = await WithRepository(repository => repository.GetAsync(notification.Id, CancellationToken));

        loaded.ShouldNotBeNull();
        loaded.CustomerId.ShouldBe(notification.CustomerId);
        loaded.Recipient.ShouldBe(notification.Recipient);
        loaded.Content.ShouldBe(notification.Content);
        loaded.IdempotencyKey.ShouldBe(notification.IdempotencyKey);
        loaded.Status.ShouldBe(NotificationStatus.Pending);
        loaded.CreatedAt.ShouldBe(notification.CreatedAt);
        loaded.NextAttemptAt.ShouldBe(notification.NextAttemptAt);
        loaded.DeliveryAttempts.ShouldBe(notification.DeliveryAttempts);
    }

    [Fact]
    public async Task Get_WhenNotFound_ReturnsNull()
    {
        var loaded = await WithRepository(repository => repository.GetAsync(NotificationId.New(), CancellationToken));

        loaded.ShouldBeNull();
    }

    [Fact]
    public async Task TryAdd_WithExistingIdempotencyKey_ReturnsFalse()
    {
        await AddAsync(TestNotifications.Email(idempotencyKey: "order-42"));

        var added = await WithRepository(repository =>
            repository.TryAddAsync(TestNotifications.Email(idempotencyKey: "order-42"), CancellationToken));

        added.ShouldBeFalse();
    }

    [Fact]
    public async Task TryAdd_WithoutIdempotencyKeys_AllowsMany()
    {
        await AddAsync(TestNotifications.Email());
        await AddAsync(TestNotifications.Email());
    }

    [Fact]
    public async Task FindByIdempotencyKey_ReturnsMatchingNotification()
    {
        var notification = TestNotifications.Email(idempotencyKey: "order-42");
        await AddAsync(notification);

        var found = await WithRepository(repository =>
            repository.FindByIdempotencyKeyAsync(IdempotencyKey.Create("order-42"), CancellationToken));

        found.ShouldNotBeNull();
        found.Id.ShouldBe(notification.Id);
    }

    [Fact]
    public async Task Update_PersistsNewAttemptsAndStatus_InOrder()
    {
        var notification = TestNotifications.Sms();
        await AddAsync(notification);

        await using (var dbContext = postgres.CreateDbContext())
        {
            var repository = new NotificationRepository(dbContext);
            var loaded = await repository.GetAsync(notification.Id, CancellationToken);
            loaded!.RecordDeliveryAttempt("Twilio", DeliveryOutcome.TransientFailure("Timeout"), Now);
            loaded.RecordDeliveryAttempt("Vonage", DeliveryOutcome.Delivered("V-1"), Now.AddSeconds(1));
            await repository.UpdateAsync(loaded, CancellationToken);
        }

        var reloaded = await WithRepository(repository => repository.GetAsync(notification.Id, CancellationToken));

        reloaded.ShouldNotBeNull();
        reloaded.Status.ShouldBe(NotificationStatus.Delivered);
        reloaded.DeliveredAt.ShouldBe(Now.AddSeconds(1));
        reloaded.DeliveryAttempts.Select(a => a.ProviderName).ShouldBe(new[] { "Twilio", "Vonage" });
        reloaded.DeliveryAttempts[1].ProviderMessageId.ShouldBe("V-1");
    }

    [Fact]
    public async Task Update_WhenChangedConcurrently_Throws()
    {
        var notification = TestNotifications.Sms();
        await AddAsync(notification);

        await using var first = postgres.CreateDbContext();
        await using var second = postgres.CreateDbContext();
        var firstRepository = new NotificationRepository(first);
        var secondRepository = new NotificationRepository(second);
        var firstCopy = await firstRepository.GetAsync(notification.Id, CancellationToken);
        var secondCopy = await secondRepository.GetAsync(notification.Id, CancellationToken);

        firstCopy!.RecordDeliveryAttempt("Twilio", DeliveryOutcome.Delivered(), Now);
        await firstRepository.UpdateAsync(firstCopy, CancellationToken);

        secondCopy!.RecordDeliveryAttempt("Vonage", DeliveryOutcome.Delivered(), Now);
        await Should.ThrowAsync<ConcurrencyConflictException>(
            () => secondRepository.UpdateAsync(secondCopy, CancellationToken));
    }

    [Fact]
    public async Task ClaimDue_ReturnsOnlyDuePendingNotifications_EarliestFirst()
    {
        var dueEarlier = TestNotifications.Sms(createdAt: Now.AddMinutes(-10));
        var dueLater = TestNotifications.Sms(createdAt: Now.AddMinutes(-1));
        var notYetDue = TestNotifications.Sms(createdAt: Now.AddMinutes(5));
        var delivered = TestNotifications.Sms(createdAt: Now.AddMinutes(-20));
        delivered.RecordDeliveryAttempt("Twilio", DeliveryOutcome.Delivered(), Now.AddMinutes(-20));
        foreach (var notification in new[] { dueLater, notYetDue, delivered, dueEarlier })
        {
            await AddAsync(notification);
        }

        var claimed = await ClaimAsync(Now, maxCount: 10);

        claimed.ShouldBe(new[] { dueEarlier.Id, dueLater.Id });
    }

    [Fact]
    public async Task ClaimDue_RespectsMaxCount()
    {
        for (var i = 0; i < 3; i++)
        {
            await AddAsync(TestNotifications.Sms(createdAt: Now.AddMinutes(-i)));
        }

        var claimed = await ClaimAsync(Now, maxCount: 2);

        claimed.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ClaimDue_DoesNotReturnClaimedNotificationsUntilLeaseExpires()
    {
        var notification = TestNotifications.Sms(createdAt: Now.AddMinutes(-1));
        await AddAsync(notification);

        (await ClaimAsync(Now, maxCount: 10)).ShouldHaveSingleItem();
        (await ClaimAsync(Now.AddMinutes(1), maxCount: 10)).ShouldBeEmpty();
        (await ClaimAsync(Now + Lease, maxCount: 10)).ShouldHaveSingleItem().ShouldBe(notification.Id);
    }

    [Fact]
    public async Task Update_ReleasesClaim()
    {
        var notification = TestNotifications.Sms(createdAt: Now.AddMinutes(-1));
        await AddAsync(notification);
        var retryPolicy = new RetryPolicy(5, TimeSpan.FromMinutes(1), TimeSpan.FromHours(1), jitterFactor: 0);

        await using (var dbContext = postgres.CreateDbContext())
        {
            var repository = new NotificationRepository(dbContext);
            var claimedId = (await repository.ClaimDueAsync(Now, 10, Lease, CancellationToken)).ShouldHaveSingleItem();
            var claimed = (await repository.GetAsync(claimedId, CancellationToken)).ShouldNotBeNull();
            claimed.ScheduleRetryOrFail(retryPolicy, "All eligible providers failed.", Now);
            await repository.UpdateAsync(claimed, CancellationToken);
        }

        // Due again after the retry delay, well before the original lease would have expired.
        (await ClaimAsync(Now.AddMinutes(1), maxCount: 10)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ClaimDue_ConcurrentClaims_NeverReturnTheSameNotification()
    {
        for (var i = 0; i < 20; i++)
        {
            await AddAsync(TestNotifications.Sms(createdAt: Now.AddSeconds(-i)));
        }

        var claims = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ClaimAsync(Now, maxCount: 5)));

        var claimedIds = claims.SelectMany(claim => claim).ToList();
        claimedIds.Count.ShouldBe(20);
        claimedIds.Distinct().Count().ShouldBe(20);
    }

    private async Task AddAsync(Notification notification)
    {
        var added = await WithRepository(repository => repository.TryAddAsync(notification, CancellationToken));
        added.ShouldBeTrue();
    }

    private Task<IReadOnlyList<NotificationId>> ClaimAsync(DateTimeOffset now, int maxCount) =>
        WithRepository(repository => repository.ClaimDueAsync(now, maxCount, Lease, CancellationToken));

    private async Task<T> WithRepository<T>(Func<NotificationRepository, Task<T>> action)
    {
        await using var dbContext = postgres.CreateDbContext();
        return await action(new NotificationRepository(dbContext));
    }
}
