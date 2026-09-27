using System.Diagnostics;
using NotificationService.Application.Providers;
using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Tests.Fakes;

/// <summary>
/// A provider whose behaviour is scripted by the test, one call at a time.
/// Once the script is used up, every call is delivered. Records every request it receives.
/// </summary>
internal sealed class FakeProvider(string name, params Channel[] supportedChannels) : INotificationProvider
{
    private readonly Queue<Func<CancellationToken, Task<DeliveryOutcome>>> _script = new();
    private readonly List<DeliveryRequest> _requests = [];

    public string Name { get; } = name;

    public IReadOnlySet<Channel> SupportedChannels { get; } = supportedChannels.ToHashSet();

    public IReadOnlyList<DeliveryRequest> Requests => _requests;

    public FakeProvider WillReturn(DeliveryOutcome outcome)
    {
        _script.Enqueue(_ => Task.FromResult(outcome));
        return this;
    }

    public FakeProvider WillFailTransiently(string reason = "Service unavailable") =>
        WillReturn(DeliveryOutcome.TransientFailure(reason));

    public FakeProvider WillThrow(Exception exception)
    {
        _script.Enqueue(_ => throw exception);
        return this;
    }

    /// <summary>Never responds, until the call is cancelled.</summary>
    public FakeProvider WillHang()
    {
        _script.Enqueue(async cancellationToken =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new UnreachableException();
        });
        return this;
    }

    public Task<DeliveryOutcome> SendAsync(DeliveryRequest request, CancellationToken cancellationToken)
    {
        _requests.Add(request);

        return _script.TryDequeue(out var next)
            ? next(cancellationToken)
            : Task.FromResult(DeliveryOutcome.Delivered($"{Name}-{_requests.Count}"));
    }
}
