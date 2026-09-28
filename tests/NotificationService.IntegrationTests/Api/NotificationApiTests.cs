using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Api.Notifications;
using NotificationService.Domain.Notifications;
using NotificationService.Infrastructure.Persistence;

namespace NotificationService.IntegrationTests.Api;

/// <summary>The HTTP contract: status codes, headers and problem details.</summary>
[Collection(ApiCollection.Name)]
public sealed class NotificationApiTests(NotificationServiceFactory factory) : ApiTestBase(factory)
{
    private const string ProblemJson = "application/problem+json";

    [Fact]
    public void Application_UsesTheTestDatabase()
    {
        // Guards the setup: a missed override would silently run the tests against a local development database.
        using var scope = Factory.Services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.GetConnectionString();

        connectionString.ShouldBe(Factory.DatabaseConnectionString);
    }

    [Fact]
    public async Task Post_ReturnsAcceptedWithLocationOfTheNotification()
    {
        using var response = await PostAsync(Sms());

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var id = (await ReadAsync<SendNotificationResponse>(response)).Id;
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/notifications/{id}");

        using var location = await Client.GetAsync(response.Headers.Location, CancellationToken);
        location.StatusCode.ShouldBe(HttpStatusCode.OK);
        var notification = await ReadAsync<NotificationResponse>(location);
        notification.Id.ShouldBe(id);
        notification.CustomerId.ShouldBe("customer-1");
        notification.Channel.ShouldBe(Channel.Sms);
        notification.Recipient.ShouldBe("+37060012345");
        notification.CreatedAt.ShouldBe(Factory.Time.GetUtcNow());
    }

    [Fact]
    public async Task Post_WithSameIdempotencyKeyAndRequest_ReturnsOriginalAndDeliversOnce()
    {
        using var first = await PostAsync(Sms(), idempotencyKey: "order-42");
        using var repeated = await PostAsync(Sms(), idempotencyKey: "order-42");

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        repeated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var id = (await ReadAsync<SendNotificationResponse>(first)).Id;
        (await ReadAsync<SendNotificationResponse>(repeated)).Id.ShouldBe(id);

        await DispatchUntilAsync(id, n => n.Status == NotificationStatus.Delivered);
        Factory.Twilio.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Post_WithSameIdempotencyKeyAndDifferentRequest_ReturnsConflict()
    {
        using var first = await PostAsync(Sms("Your code is 123456"), idempotencyKey: "order-42");
        using var conflicting = await PostAsync(Sms("Your code is 654321"), idempotencyKey: "order-42");

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await ShouldBeProblemAsync(conflicting, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Post_WithInvalidPhoneNumber_ReturnsBadRequestWithoutEchoingIt()
    {
        using var response = await PostAsync(new { customerId = "customer-1", channel = "Sms", recipient = "12345", body = "Hello" });

        var problem = await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest);
        problem.Detail.ShouldBe("Phone number must be in E.164 format, e.g. +37060012345.");
    }

    [Fact]
    public async Task Post_EmailWithoutSubject_ReturnsBadRequest()
    {
        using var response = await PostAsync(new { customerId = "customer-1", channel = "Email", recipient = "jane@example.com", body = "Hello" });

        var problem = await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest);
        problem.Detail.ShouldBe("Email notifications require a subject.");
    }

    [Fact]
    public async Task Post_WithoutChannel_ReturnsValidationProblem()
    {
        using var response = await PostAsync(new { customerId = "customer-1", recipient = "+37060012345", body = "Hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json, CancellationToken)).ShouldNotBeNull();
        problem.Errors.ShouldContainKey("channel");
    }

    [Theory]
    [InlineData("""{ "customerId": "customer-1", "channel": "Pigeon", "recipient": "+37060012345", "body": "Hello" }""")]
    [InlineData("""{ "customerId": "customer-1", "channel": 1, "recipient": "+37060012345", "body": "Hello" }""")]
    public async Task Post_WithUnknownChannel_ReturnsBadRequestNamingTheField(string json)
    {
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync("/notifications", content, CancellationToken);

        var problem = await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest);
        problem.Detail.ShouldNotBeNull().ShouldContain("$.channel");
    }

    [Fact]
    public async Task Post_WithMalformedJson_ReturnsBadRequest()
    {
        using var content = new StringContent("{ not json", Encoding.UTF8, "application/json");
        using var response = await Client.PostAsync("/notifications", content, CancellationToken);

        await ShouldBeProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_UnknownNotification_ReturnsNotFound()
    {
        using var response = await Client.GetAsync($"/notifications/{Guid.NewGuid()}", CancellationToken);

        await ShouldBeProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthChecks_AreHealthy(string path)
    {
        using var response = await Client.GetAsync(path, CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(CancellationToken)).ShouldBe("Healthy");
    }

    private static async Task<ProblemDetails> ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode statusCode)
    {
        response.StatusCode.ShouldBe(statusCode);
        response.Content.Headers.ContentType?.MediaType.ShouldBe(ProblemJson);

        var problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(Json, CancellationToken)).ShouldNotBeNull();
        problem.Status.ShouldBe((int)statusCode);
        return problem;
    }
}
