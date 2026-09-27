using NotificationService.Application.Providers;
using NotificationService.Domain.Notifications;

namespace NotificationService.IntegrationTests.Fakes;

/// <summary>A provider whose behaviour is set by the test. Delivers by default. Safe to call concurrently.</summary>
internal sealed class TestProvider(string name, params Channel[] supportedChannels) : INotificationProvider
{
    private int _callCount;

    public string Name { get; } = name;

    public IReadOnlySet<Channel> SupportedChannels { get; } = supportedChannels.ToHashSet();

    public Func<DeliveryRequest, CancellationToken, Task<DeliveryOutcome>> Behavior { get; set; } =
        (request, _) => Task.FromResult(DeliveryOutcome.Delivered($"test-{request.NotificationId}"));

    public int CallCount => Volatile.Read(ref _callCount);

    public Task<DeliveryOutcome> SendAsync(DeliveryRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        return Behavior(request, cancellationToken);
    }
}
