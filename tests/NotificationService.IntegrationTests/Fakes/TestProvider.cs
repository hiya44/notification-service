using NotificationService.Application.Providers;
using NotificationService.Domain.Notifications;

namespace NotificationService.IntegrationTests.Fakes;

/// <summary>A provider whose behaviour is set by the test. Delivers by default. Safe to call concurrently.</summary>
public sealed class TestProvider(string name, params Channel[] supportedChannels) : INotificationProvider
{
    private int _callCount;

    public string Name { get; } = name;

    public IReadOnlySet<Channel> SupportedChannels { get; } = supportedChannels.ToHashSet();

    public Func<DeliveryRequest, CancellationToken, Task<DeliveryOutcome>> Behavior { get; set; } = Deliver;

    public int CallCount => Volatile.Read(ref _callCount);

    public void AlwaysDeliver() => Behavior = Deliver;

    public void AlwaysFailTransiently(string reason = "Service unavailable") =>
        Behavior = (_, _) => Task.FromResult(DeliveryOutcome.TransientFailure(reason));

    public void AlwaysFailPermanently(string reason = "Recipient does not exist") =>
        Behavior = (_, _) => Task.FromResult(DeliveryOutcome.PermanentFailure(reason));

    /// <summary>Back to delivering every notification, with the call count cleared.</summary>
    public void Reset()
    {
        AlwaysDeliver();
        Interlocked.Exchange(ref _callCount, 0);
    }

    private static Task<DeliveryOutcome> Deliver(DeliveryRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(DeliveryOutcome.Delivered($"test-{request.NotificationId}"));

    public Task<DeliveryOutcome> SendAsync(DeliveryRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        return Behavior(request, cancellationToken);
    }
}
