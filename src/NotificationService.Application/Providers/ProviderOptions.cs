using NotificationService.Domain.Notifications;

namespace NotificationService.Application.Providers;

public sealed class ProviderOptions
{
    public bool Enabled { get; init; } = true;

    /// <summary>Lower values are tried first (1 = primary).</summary>
    public int Priority { get; init; }

    /// <summary>Channels this provider should be used for. Must be a subset of what it supports.</summary>
    public HashSet<Channel> Channels { get; init; } = [];
}
