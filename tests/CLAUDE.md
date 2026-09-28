# Tests

| Project | Level | Docker |
|---|---|---|
| `NotificationService.Domain.Tests` | Domain unit tests | No |
| `NotificationService.Application.Tests` | Selection, failover, retry, use cases, with fakes | No |
| `NotificationService.Infrastructure.Tests` | Providers, SMTP classification, DI and startup validation | No |
| `NotificationService.IntegrationTests` | PostgreSQL and Mailpit (Testcontainers), worker, end-to-end (`Api/`) | Yes |

Test the behaviour at the lowest level that can show it, and add an end-to-end test when a change crosses layers.

## Conventions

- **xUnit v3** (test projects are executables) and **Shouldly** for assertions. Not FluentAssertions, because of its commercial licence.
- **Hand-written fakes, no mocking library:** `FakeProvider` (`.WillFailTransiently()`, `.WillThrow()`, `.WillHang()`),
  `InMemoryNotificationRepository`, `TestOptionsMonitor`, and `TestProvider` in the integration tests.
- **Time is always controlled:** `FakeTimeProvider` and fixed times (`TestNotifications.Now`); never `DateTime.UtcNow` or real delays to wait for an outcome.
- **Pass `TestContext.Current.CancellationToken`** to async calls; the xUnit1051 analyzer makes forgetting it a build error.
- **Test names follow `Method_Scenario_ExpectedResult`.**

## Integration and end-to-end tests

- **Testcontainers:** `new PostgreSqlBuilder("postgres:17-alpine")` (the parameterless constructor is obsolete, and warnings are errors).
  `Testcontainers` >= 4.15.0; older versions pull a vulnerable SSH.NET.
- **`Api/NotificationServiceFactory`** runs the real application in a `Testing` environment. It replaces only the providers
  (fakes under the real names, so the real configuration routes notifications) and the clock (`FakeTimeProvider`).
  Tests in the `Api` collection share one application and run one at a time.
- **Observe results only through HTTP,** and wait for the background worker by polling with a timeout (`ApiTestBase.WaitUntilAsync`),
  never with fixed sleeps. Advance the fake clock to trigger polling (`DispatchUntilAsync`) and retries (`AdvancePast`).
- **xUnit fixtures and the types they expose must be public.**
- **Re-run new end-to-end tests several times** before handing them over, to catch flakiness.
