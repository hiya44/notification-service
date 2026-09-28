using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NotificationService.Api.Notifications;

namespace NotificationService.IntegrationTests.Api;

public abstract class ApiTestBase(NotificationServiceFactory factory) : IAsyncLifetime
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    protected NotificationServiceFactory Factory { get; } = factory;

    protected HttpClient Client { get; } = factory.CreateClient();

    protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await Factory.ResetAsync();

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        return ValueTask.CompletedTask;
    }

    protected static object Sms(string body = "Your code is 123456") =>
        new { customerId = "customer-1", channel = "Sms", recipient = "+37060012345", body };

    protected static object Email(string body = "It will arrive tomorrow.") =>
        new { customerId = "customer-1", channel = "Email", recipient = "jane@example.com", subject = "Your order has shipped", body };

    protected async Task<HttpResponseMessage> PostAsync(object request, string? idempotencyKey = null)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/notifications") { Content = JsonContent.Create(request) };
        if (idempotencyKey is not null)
        {
            message.Headers.Add(NotificationEndpoints.IdempotencyKeyHeader, idempotencyKey);
        }

        return await Client.SendAsync(message, CancellationToken);
    }

    /// <summary>Posts a notification that must be accepted, and returns its id.</summary>
    protected async Task<Guid> SendAsync(object request)
    {
        using var response = await PostAsync(request);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Accepted);
        return (await ReadAsync<SendNotificationResponse>(response)).Id;
    }

    protected async Task<NotificationResponse> GetAsync(Guid id)
    {
        using var response = await Client.GetAsync($"/notifications/{id}", CancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<NotificationResponse>(response);
    }

    protected static async Task<T> ReadAsync<T>(HttpResponseMessage response)
        where T : class =>
        (await response.Content.ReadFromJsonAsync<T>(Json, CancellationToken)).ShouldNotBeNull();

    /// <summary>Moves the clock past <paramref name="dueAt"/> and a polling interval, so the worker polls once it is due.</summary>
    protected void AdvancePast(DateTimeOffset dueAt)
    {
        var untilDue = dueAt - Factory.Time.GetUtcNow();
        Factory.Time.Advance((untilDue > TimeSpan.Zero ? untilDue : TimeSpan.Zero) + NotificationServiceFactory.PollingInterval);
    }

    /// <summary>Lets the worker run its next poll, and waits (via the API) until the notification satisfies the condition.</summary>
    protected async Task<NotificationResponse> DispatchUntilAsync(Guid id, Func<NotificationResponse, bool> condition)
    {
        Factory.Time.Advance(NotificationServiceFactory.PollingInterval);
        return await WaitUntilAsync(id, condition);
    }

    /// <summary>The worker runs in the background, so results are awaited by polling the API (in real time).</summary>
    protected async Task<NotificationResponse> WaitUntilAsync(Guid id, Func<NotificationResponse, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var notification = await GetAsync(id);
            if (condition(notification))
            {
                return notification;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"Notification {id} did not reach the expected state; it is {notification.Status} " +
                    $"with {notification.DeliveryAttempts.Count} attempt(s).");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), CancellationToken);
        }
    }
}
