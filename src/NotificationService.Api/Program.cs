using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using NotificationService.Api.Dispatching;
using NotificationService.Api.ErrorHandling;
using NotificationService.Api.Notifications;
using NotificationService.Application;
using NotificationService.Infrastructure;
using NotificationService.Infrastructure.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Notifications")
    ?? throw new InvalidOperationException("Connection string 'Notifications' is not configured.");

builder.Services
    .AddNotificationApplication()
    .AddNotificationPersistence(connectionString)
    .AddNotificationProviders(builder.Configuration)
    .AddDispatchWorker(builder.Configuration);

// Enums as names ("Sms", "Delivered") in requests and responses; numbers are rejected so a typo cannot map to a wrong value.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// By default, minimal APIs only throw for unreadable requests in Development and otherwise return a bare 400.
// Throwing everywhere lets ApiExceptionHandler return the same problem details (naming the invalid field) in every environment.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<NotificationDbContext>("database", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();

    // Convenient locally; in production migrations would run as a separate deployment step,
    // so several instances starting at once do not race and the app does not need schema-changing permissions.
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<NotificationDbContext>().Database.MigrateAsync();
}

app.MapNotificationEndpoints();

// Liveness: the process is up. Readiness: it can also reach the database, so it can accept notifications.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();
