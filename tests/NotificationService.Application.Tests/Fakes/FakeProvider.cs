using NotificationService.Application.Providers;
using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Tests.Fakes;

/// <summary>
/// A provider whose outcomes are scripted by the test. Records every request it receives.
/// </summary>
internal sealed class FakeProvider(string name, params Channel[] supportedChannels) : INotificationProvider
{
    private readonly List<DeliveryRequest> _requests = [];

    public string Name { get; } = name;

    public IReadOnlySet<Channel> SupportedChannels { get; } = supportedChannels.ToHashSet();

    public IReadOnlyList<DeliveryRequest> Requests => _requests;

    public Task<DeliveryOutcome> SendAsync(DeliveryRequest request, CancellationToken cancellationToken)
    {
        _requests.Add(request);
        return Task.FromResult(DeliveryOutcome.Delivered($"{Name}-{_requests.Count}"));
    }
}
