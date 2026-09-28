using NotificationService.Domain.Notifications;

namespace NotificationService.IntegrationTests.Api;

/// <summary>
/// End to end through the running application: POST -> stored -> background worker -> providers -> saved -> GET.
/// Providers are configured as in appsettings.json: SMS via Twilio (priority 1) then Vonage; email via Smtp then AmazonSes.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DeliveryPipelineTests(NotificationServiceFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Sms_IsDeliveredByHighestPriorityProvider()
    {
        var id = await SendAsync(Sms());

        var notification = await DispatchUntilAsync(id, n => n.Status != NotificationStatus.Pending);

        notification.Status.ShouldBe(NotificationStatus.Delivered);
        notification.DeliveredAt.ShouldNotBeNull();
        var attempt = notification.DeliveryAttempts.ShouldHaveSingleItem();
        attempt.Provider.ShouldBe("Twilio");
        attempt.Outcome.ShouldBe(DeliveryOutcomeKind.Delivered);
        attempt.ProviderMessageId.ShouldBe($"test-{id}");
        Factory.Vonage.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Email_IsDeliveredByEmailProvider()
    {
        var id = await SendAsync(Email());

        var notification = await DispatchUntilAsync(id, n => n.Status != NotificationStatus.Pending);

        notification.Status.ShouldBe(NotificationStatus.Delivered);
        notification.DeliveryAttempts.ShouldHaveSingleItem().Provider.ShouldBe("Smtp");
        Factory.Twilio.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task Sms_WhenPrimaryProviderFails_FailsOverToNextProvider()
    {
        Factory.Twilio.AlwaysFailTransiently("Twilio is down");
        var id = await SendAsync(Sms());

        var notification = await DispatchUntilAsync(id, n => n.Status != NotificationStatus.Pending);

        notification.Status.ShouldBe(NotificationStatus.Delivered);
        notification.DeliveryAttempts.Select(a => (a.Provider, a.Outcome, a.FailureReason)).ShouldBe(
        [
            ("Twilio", DeliveryOutcomeKind.TransientFailure, "Twilio is down"),
            ("Vonage", DeliveryOutcomeKind.Delivered, null),
        ]);
        notification.FailedDispatchCount.ShouldBe(0);
    }

    [Fact]
    public async Task Sms_WhenAllProvidersFail_IsRetriedLaterAndDelivered()
    {
        Factory.Twilio.AlwaysFailTransiently();
        Factory.Vonage.AlwaysFailTransiently();
        var id = await SendAsync(Sms());

        var afterFirstDispatch = await DispatchUntilAsync(id, n => n.FailedDispatchCount == 1);

        afterFirstDispatch.Status.ShouldBe(NotificationStatus.Pending);
        afterFirstDispatch.DeliveryAttempts.Select(a => a.Provider).ShouldBe(["Twilio", "Vonage"]);
        afterFirstDispatch.NextAttemptAt.ShouldBe(afterFirstDispatch.DeliveryAttempts[^1].AttemptedAt + NotificationServiceFactory.InitialRetryDelay);

        // Not retried before it is due, although the worker keeps polling.
        Factory.Time.Advance(NotificationServiceFactory.PollingInterval);
        await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken);
        (await GetAsync(id)).DeliveryAttempts.Count.ShouldBe(2);
        Factory.Twilio.CallCount.ShouldBe(1);

        // The primary has recovered: the retry starts again from the highest-priority provider.
        Factory.Twilio.AlwaysDeliver();
        AdvancePast(afterFirstDispatch.NextAttemptAt!.Value);
        var delivered = await WaitUntilAsync(id, n => n.Status != NotificationStatus.Pending);

        delivered.Status.ShouldBe(NotificationStatus.Delivered);
        delivered.DeliveryAttempts.Select(a => (a.Provider, a.Outcome)).ShouldBe(
        [
            ("Twilio", DeliveryOutcomeKind.TransientFailure),
            ("Vonage", DeliveryOutcomeKind.TransientFailure),
            ("Twilio", DeliveryOutcomeKind.Delivered),
        ]);
    }

    [Fact]
    public async Task Sms_WhenAllProvidersKeepFailing_FailsAfterMaxDispatches()
    {
        Factory.Twilio.AlwaysFailTransiently();
        Factory.Vonage.AlwaysFailTransiently();
        var id = await SendAsync(Sms());

        var notification = await DispatchUntilAsync(id, n => n.FailedDispatchCount == 1);
        for (var dispatch = 2; dispatch <= NotificationServiceFactory.MaxDispatches; dispatch++)
        {
            var expected = dispatch;
            AdvancePast(notification.NextAttemptAt!.Value);
            notification = await WaitUntilAsync(id, n => n.FailedDispatchCount == expected);
        }

        notification.Status.ShouldBe(NotificationStatus.Failed);
        notification.NextAttemptAt.ShouldBeNull();
        notification.FailureReason.ShouldNotBeNull();
        notification.DeliveryAttempts.Count.ShouldBe(NotificationServiceFactory.MaxDispatches * 2);

        // Given up: nothing is attempted any more.
        Factory.Time.Advance(TimeSpan.FromHours(2));
        await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken);
        (await GetAsync(id)).DeliveryAttempts.Count.ShouldBe(NotificationServiceFactory.MaxDispatches * 2);
    }

    [Fact]
    public async Task Sms_WhenProviderReportsPermanentFailure_FailsWithoutTryingOtherProviders()
    {
        Factory.Twilio.AlwaysFailPermanently("Number does not exist");
        var id = await SendAsync(Sms());

        var notification = await DispatchUntilAsync(id, n => n.Status != NotificationStatus.Pending);

        notification.Status.ShouldBe(NotificationStatus.Failed);
        notification.FailureReason.ShouldBe("Number does not exist");
        notification.DeliveryAttempts.ShouldHaveSingleItem().Outcome.ShouldBe(DeliveryOutcomeKind.PermanentFailure);
        Factory.Vonage.CallCount.ShouldBe(0);
    }
}
