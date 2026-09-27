using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotificationService.Application.Providers;
using NotificationService.Domain.Notifications;
using NotificationService.Infrastructure.Providers.Smtp;

namespace NotificationService.IntegrationTests.Providers;

public sealed class SmtpEmailProviderTests(MailpitFixture mailpit) : IClassFixture<MailpitFixture>, IAsyncLifetime
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await mailpit.DeleteAllMessagesAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SendAsync_DeliversEmailThroughSmtpServer()
    {
        var provider = CreateProvider(new SmtpOptions { Host = mailpit.Host, Port = mailpit.Port, FromAddress = "noreply@example.com" });
        var request = EmailTo("jane@example.com");

        var outcome = await provider.SendAsync(request, CancellationToken);

        outcome.Kind.ShouldBe(DeliveryOutcomeKind.Delivered);

        var message = (await mailpit.GetMessagesAsync()).ShouldHaveSingleItem();
        message.Subject.ShouldBe("Your order has shipped");
        message.From.Address.ShouldBe("noreply@example.com");
        message.To.ShouldHaveSingleItem().Address.ShouldBe("jane@example.com");
        message.MessageID.ShouldBe(outcome.ProviderMessageId);
        (await mailpit.GetMessageAsync(message.ID)).Text.ShouldContain("It will arrive tomorrow.");
        (await mailpit.GetHeadersAsync(message.ID))[SmtpEmailProvider.NotificationIdHeader]
            .ShouldBe([request.NotificationId.ToString()]);
    }

    [Fact]
    public async Task SendAsync_WhenServerRejectsRecipient_IsPermanentFailure()
    {
        var provider = CreateProvider(new SmtpOptions { Host = mailpit.Host, Port = mailpit.Port });

        var outcome = await provider.SendAsync(EmailTo("jane@not-accepted.test"), CancellationToken);

        outcome.Kind.ShouldBe(DeliveryOutcomeKind.PermanentFailure);
        outcome.FailureReason.ShouldNotBeNull().ShouldNotContain("jane@not-accepted.test");
        (await mailpit.GetMessagesAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task SendAsync_WhenServerUnreachable_IsTransientFailure()
    {
        var provider = CreateProvider(new SmtpOptions { Host = "127.0.0.1", Port = GetUnusedPort() });

        var outcome = await provider.SendAsync(EmailTo("jane@example.com"), CancellationToken);

        outcome.Kind.ShouldBe(DeliveryOutcomeKind.TransientFailure);
    }

    private static SmtpEmailProvider CreateProvider(SmtpOptions options) =>
        new(new StaticOptionsMonitor<SmtpOptions>(options), NullLogger<SmtpEmailProvider>.Instance);

    private static DeliveryRequest EmailTo(string address) => new(
        NotificationId.New(),
        Recipient.Email(EmailAddress.Create(address)),
        NotificationContent.Create("Your order has shipped", "It will arrive tomorrow."));

    private static int GetUnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class StaticOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue => value;

        public TOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
