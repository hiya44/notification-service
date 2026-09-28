using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NotificationService.Application.Notifications;
using NotificationService.Domain.Common;

namespace NotificationService.Api.ErrorHandling;

/// <summary>
/// Turns expected exceptions into problem details responses: invalid or unreadable requests (400) and idempotency key conflicts (409).
/// Anything else falls through to the default handler, which returns a 500 without internal details.
/// Domain exception messages are safe to return because they never contain personal data.
/// </summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails? problem = exception switch
        {
            DomainException => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The notification request is invalid.",
                Detail = exception.Message,
            },
            IdempotencyKeyConflictException => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "The idempotency key was already used for a different request.",
                Detail = "Use a new idempotency key for a different notification, or repeat the original request unchanged.",
            },
            // Malformed JSON or a value of the wrong type (e.g. an unknown channel). Without this it would become a 500.
            BadHttpRequestException badRequest => new ProblemDetails
            {
                Status = badRequest.StatusCode,
                Title = "The request could not be read.",
                Detail = badRequest.InnerException is JsonException { Path: { } path }
                    ? $"The value at '{path}' is missing or has the wrong format."
                    : "The request body is not valid JSON for this endpoint.",
            },
            _ => null,
        };

        if (problem is null)
        {
            return false;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }
}
