using Microsoft.Extensions.Options;

namespace NotificationService.Infrastructure.Tests.Fakes;

/// <summary>An options monitor whose value can be replaced during a test, simulating a configuration reload.</summary>
internal sealed class TestOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
{
    public TOptions CurrentValue { get; set; } = currentValue;

    public TOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
}
