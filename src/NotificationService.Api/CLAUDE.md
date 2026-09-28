# Api layer

The host: HTTP endpoints, error handling, the background dispatch worker, and the composition root (`Program.cs`).

## Endpoints

- **Minimal APIs in `Notifications/`,** with request and response contracts kept separate from the application's read model.
- **`POST /notifications`** returns 202 with a `Location` header (named route `GetNotification`), or 200 when an idempotency key
  was already used for the same request. The contract is public: changing it needs the developer's agreement.
- **Validation lives in the domain.** Endpoints do not re-validate; `ErrorHandling/ApiExceptionHandler` maps `DomainException` -> 400,
  `IdempotencyKeyConflictException` -> 409 and `BadHttpRequestException` -> 400, all as problem details.
- **Enums are strings,** and numbers are rejected (`JsonStringEnumConverter(allowIntegerValues: false)`).
- OpenAPI, Scalar and automatic migrations are enabled in Development only.
- Keep `NotificationService.Api.http` in step with the endpoints; it is the demo script.

### Pitfall

Minimal APIs only throw `BadHttpRequestException` for unreadable JSON in Development; elsewhere they return a bare 400. `Program.cs`
sets `RouteHandlerOptions.ThrowOnBadRequest = true` in every environment so `ApiExceptionHandler` handles it consistently
(unmapped, `UseExceptionHandler` would turn it into a 500). A manual check in Development alone would not catch a regression; the
end-to-end tests run in a `Testing` environment for this reason.

## Dispatch worker

- **`Dispatching/DispatchWorker`** polls on a `PeriodicTimer`, claims a batch in a short-lived scope, then dispatches every claimed id
  **concurrently**, each in its own DI scope ([ADR 0005](../../docs/adr/0005-one-unit-of-work-per-notification.md)).
- **One failing notification must never stop the batch,** and a failed poll must never stop the worker: log and continue.
- **`WorkerOptionsValidator` requires the lease to outlast the longest dispatch** (attempt timeout x enabled providers per channel).
  Keep that rule true when changing timeouts or retry behaviour.
- `DispatchWorker` is also registered as itself, so tests can call `ProcessDueNotificationsAsync` directly.
