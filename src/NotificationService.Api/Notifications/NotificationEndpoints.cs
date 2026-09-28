using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Application.Notifications;
using NotificationService.Domain.Notifications;

namespace NotificationService.Api.Notifications;

public static class NotificationEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private const string GetNotificationRoute = "GetNotification";

    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var notifications = endpoints.MapGroup("/notifications").WithTags("Notifications");

        notifications.MapPost("/", SendAsync)
            .WithSummary("Accept a notification for delivery")
            .WithDescription(
                "Validates and stores the notification; delivery happens in the background. " +
                $"Send an {IdempotencyKeyHeader} header to make retries of the same request safe: " +
                "a repeated request returns the original notification (200) instead of creating a new one.")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        notifications.MapGet("/{id:guid}", GetAsync)
            .WithName(GetNotificationRoute)
            .WithSummary("Get a notification's status and delivery history")
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <remarks>
    /// Invalid values are rejected by the domain (<see cref="Domain.Common.DomainException"/>) and an idempotency key reused for
    /// a different request by <see cref="IdempotencyKeyConflictException"/>; <see cref="ErrorHandling.ApiExceptionHandler"/>
    /// turns them into 400 and 409 responses, so the rules live in one place.
    /// </remarks>
    private static async Task<Results<Accepted<SendNotificationResponse>, Ok<SendNotificationResponse>, ValidationProblem>> SendAsync(
        SendNotificationRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        SendNotificationHandler handler,
        LinkGenerator links,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request.Channel is not { } channel)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["channel"] = ["Channel is required: Sms or Email."],
            });
        }

        var result = await handler.HandleAsync(
            new SendNotificationCommand(request.CustomerId, channel, request.Recipient, request.Subject, request.Body, idempotencyKey),
            cancellationToken);

        var response = new SendNotificationResponse(result.NotificationId.Value);

        if (result.IsDuplicate)
        {
            return TypedResults.Ok(response);
        }

        var location = links.GetPathByName(httpContext, GetNotificationRoute, new { id = result.NotificationId.Value });
        return TypedResults.Accepted(location, response);
    }

    private static async Task<Results<Ok<NotificationResponse>, NotFound>> GetAsync(
        Guid id,
        GetNotificationHandler handler,
        CancellationToken cancellationToken)
    {
        var details = await handler.HandleAsync(new NotificationId(id), cancellationToken);

        return details is null ? TypedResults.NotFound() : TypedResults.Ok(NotificationResponse.From(details));
    }
}
