using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace NotificationService.IntegrationTests.Providers;

/// <summary>
/// Starts a disposable Mailpit SMTP server. Only recipients at example.com are accepted,
/// so the tests can also exercise a recipient rejected by the server.
/// </summary>
public sealed class MailpitFixture : IAsyncLifetime
{
    private const int SmtpPort = 1025;
    private const int HttpPort = 8025;

    private readonly IContainer _container = new ContainerBuilder("axllent/mailpit:latest")
        .WithPortBinding(SmtpPort, assignRandomHostPort: true)
        .WithPortBinding(HttpPort, assignRandomHostPort: true)
        .WithEnvironment("MP_SMTP_ALLOWED_RECIPIENTS", @"@example\.com$")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(HttpPort).ForPath("/readyz")))
        .Build();

    private HttpClient? _api;

    public string Host => _container.Hostname;

    public int Port => _container.GetMappedPublicPort(SmtpPort);

    private HttpClient Api => _api ?? throw new InvalidOperationException("Mailpit has not been started.");

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);
        _api = new HttpClient { BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(HttpPort)}") };
    }

    public async ValueTask DisposeAsync()
    {
        _api?.Dispose();
        await _container.DisposeAsync();
    }

    public async Task DeleteAllMessagesAsync() =>
        (await Api.DeleteAsync("/api/v1/messages", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

    public async Task<IReadOnlyList<MailpitMessage>> GetMessagesAsync()
    {
        var list = await Api.GetFromJsonAsync<MailpitMessageList>("/api/v1/messages", TestContext.Current.CancellationToken);
        return list?.Messages ?? [];
    }

    public async Task<MailpitMessageDetails> GetMessageAsync(string id) =>
        await Api.GetFromJsonAsync<MailpitMessageDetails>($"/api/v1/message/{id}", TestContext.Current.CancellationToken)
        ?? throw new InvalidOperationException($"Message {id} not found.");

    public async Task<IReadOnlyDictionary<string, string[]>> GetHeadersAsync(string id) =>
        await Api.GetFromJsonAsync<Dictionary<string, string[]>>($"/api/v1/message/{id}/headers", TestContext.Current.CancellationToken)
        ?? [];
}

public sealed record MailpitMessageList(IReadOnlyList<MailpitMessage> Messages);

public sealed record MailpitMessage(string ID, string MessageID, string Subject, MailpitAddress From, IReadOnlyList<MailpitAddress> To);

public sealed record MailpitMessageDetails(string Text);

public sealed record MailpitAddress(string Name, string Address);
