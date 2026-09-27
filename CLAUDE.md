# CLAUDE.md

Context for AI assistants working in this repository. This is a take-home task for a .NET back-end position:
a **Notification Service** that accepts requests to notify customers and delivers them over SMS or email
through configurable, prioritised providers with failover and retries. The original task text asks for:
at least two channels, provider abstraction, failover by priority, retry later when no provider can deliver,
configurable providers (enabled, priority, channels), DDD and a clear ubiquitous language, automated tests
(especially provider selection, failover and retry), a README with design decisions/trade-offs, a README
section on AI usage, and a Git history that shows how the solution evolved (no squashing).

## How we work (important)

- **The developer reviews every step before it is committed. Never run `git commit`, `git push`, `git reset`,
  `git checkout -- .` or any other command that changes Git history or discards changes.** Leave committing to the developer.
- Work in small steps; each step ends in one commit (conventional commits: `feat(domain): ...`, `feat(app): ...`,
  `feat(infra): ...`, `feat(api): ...`, `test: ...`, `fix(...): ...`, `refactor: ...`, `docs: ...`).
  Suggest the commit message at the end of the step.
- Before handing a step over, run `dotnet build` and `dotnet test` and make sure both pass.
- At the end of a step, summarise what changed and the design decisions the developer should be ready to defend in the interview.
- Keep the README up to date in the same step as the change it documents.
- The developer uses Visual Studio 2026 with the solution open. Files open in VS with unsaved edits can be saved back
  over external changes; if a change seems to be missing, check the file on disk before debugging further.

## Commands

```powershell
docker compose up -d        # PostgreSQL 17 (localhost:5432) + Mailpit (SMTP localhost:1025, UI http://localhost:8025)
dotnet build
dotnet test                 # integration tests start their own PostgreSQL via Testcontainers (Docker must be running)
dotnet tool restore
dotnet ef migrations add <Name> --project src/NotificationService.Infrastructure --startup-project src/NotificationService.Api --output-dir Persistence/Migrations
```

## Architecture

Hexagonal / clean architecture, dependencies point inwards:

```
src/
  NotificationService.Domain          aggregate, value objects, RetryPolicy, INotificationRepository (no dependencies)
  NotificationService.Application     use cases, provider port + selection, dispatcher, options
  NotificationService.Infrastructure  EF Core + PostgreSQL persistence; providers (simulated + SMTP) and AddNotificationProviders
  NotificationService.Api             ASP.NET Core host: DispatchWorker + AddDispatchWorker (step 8); endpoints in step 9
tests/
  NotificationService.Domain.Tests
  NotificationService.Application.Tests   selection, failover, retry, use cases (fakes, FakeTimeProvider)
  NotificationService.Infrastructure.Tests simulated providers, SMTP error classification, startup validation (no Docker)
  NotificationService.IntegrationTests    Testcontainers PostgreSQL and Mailpit (SMTP); WebApplicationFactory in step 10
```

Flow: `POST /notifications` -> `SendNotificationHandler` validates and stores a `Pending` notification (202 Accepted) ->
a background worker claims due notifications -> `NotificationDispatcher` tries eligible providers in priority order ->
`Delivered`, `Failed` (permanent failure or retries exhausted), or a retry is scheduled with backoff.

### Key decisions (already implemented; keep consistent)

- **Asynchronous delivery**: sending only validates and stores; delivery happens in the background. REST only (no broker);
  the application layer is transport-agnostic so a messaging consumer could be added later.
- **Domain is pure**: methods take `DateTimeOffset now` instead of reading a clock. The application layer uses `TimeProvider`.
- **`Notification` aggregate**: `RecordDeliveryAttempt(provider, outcome, at)` and `ScheduleRetryOrFail(policy, reason, now)`.
  `DeliveryOutcome` is `Delivered`, `TransientFailure` (try next provider / retry later) or `PermanentFailure`
  (recipient-level problem, fail immediately without trying other providers).
- **`RetryPolicy`**: exponential backoff with ±20% jitter capped at a max delay; `MaxDispatches` includes the first dispatch.
  Retries count failed *dispatches*, not provider calls. "No eligible provider" also schedules a retry.
- **Providers**: `INotificationProvider` (`Name`, `SupportedChannels`, `SendAsync(DeliveryRequest, ct)`). Providers receive a
  `DeliveryRequest`, never the aggregate. `ProviderSelector` returns providers that are configured, enabled, configured
  for the channel and technically support it, ordered by `Priority` (lower first) then name. Unconfigured providers are never used.
  Configuration is read via `IOptionsMonitor` on every dispatch (hot reload).
- **Dispatcher**: per-attempt timeout via `WaitAsync(timeout, timeProvider)`; timeouts and unexpected exceptions are transient
  failures (only the exception type is stored, details are logged); shutdown cancellation propagates and schedules nothing.
  It only mutates the aggregate in memory; the caller persists it.
- **Idempotency**: optional caller-supplied key; same key + same request returns the original (`IsDuplicate`), same key +
  different request throws `IdempotencyKeyConflictException` (-> 409). A unique index settles races (`TryAddAsync` returns false).
- **Persistence**: EF Core 10 + Npgsql. Recipient and content are EF complex types; delivery attempts are an owned collection.
  `locked_until` (lease) and `xmin` (optimistic concurrency, via `IsRowVersion`) are shadow properties, not in the domain.
  `ClaimDueAsync` uses one `UPDATE ... WHERE id IN (SELECT ... FOR UPDATE SKIP LOCKED) RETURNING id` (in a CTE to order
  earliest-first) and returns **ids only**; each notification is loaded separately (LINQ) in its own scope.
  `UpdateAsync` clears the lease and maps `DbUpdateConcurrencyException` to `ConcurrencyConflictException`.
  Repository methods save immediately (no separate unit of work).
- **Assumption**: the caller sends the recipient's contact address; this service does not own customer data (`CustomerId` is for traceability).
- **Providers (step 7)**: simulated settings are named options bound from `Notifications:Providers:{Name}:Simulation`
  (next to the routing settings; the `ProviderOptions` binder ignores the extra key). `NotificationOptionsValidator` lives in
  Application (needs only the provider port); `AddNotificationProviders(configuration)` in Infrastructure registers providers
  as singletons with `ValidateOnStart`. Not yet wired into `Program.cs` (step 9). SMTP: only 5xx `RecipientNotAccepted` is permanent.
- **Worker (step 8)**: `DispatchWorker` (Api) claims a batch in a short scope, then dispatches all claimed ids **concurrently**
  (`Task.WhenAll`), each in its own DI scope via `DispatchNotificationHandler` (Application: load -> re-check `IsDueAt` ->
  dispatch -> `UpdateAsync`; logs and swallows `ConcurrencyConflictException`). Full batch -> claim again without waiting.
  Per-notification exceptions are logged; the lease expires and it is retried. Poll failures are logged, worker stays alive.
  `WorkerOptionsValidator`: lease > `DeliveryAttemptTimeout` x max enabled providers per channel. `AddNotificationApplication()`
  (Application) registers selector/dispatcher as singletons and handlers as scoped. `DispatchWorker` is registered as itself too
  so tests call `ProcessDueNotificationsAsync` (internal) directly. Not yet wired into `Program.cs` (step 9).
- **At-least-once delivery**: a crash after a provider accepted a message but before saving can cause a duplicate send. Documented trade-off.

### Ubiquitous language

Notification, Channel (`Sms`, `Email`), Recipient, Content, Provider, Delivery attempt, Delivery outcome, Dispatch
(one pass over eligible providers), Retry (a later dispatch), Idempotency key, Eligible provider. Use these names in code and docs.

## Conventions

- .NET 10, C# latest, nullable enabled, **warnings are errors**, code style enforced in build (file-scoped namespaces,
  readonly fields). EF migrations are excluded via `.editorconfig` (`generated_code = true`).
- **Central package management**: versions only in `Directory.Packages.props`; add packages in the step that first needs them.
  NuGet vulnerability warnings fail the build - pick patched versions.
- Tests: **xUnit v3** (test projects are `Exe`), **Shouldly** (not FluentAssertions - commercial licence), hand-written fakes
  (`FakeProvider` with `.WillFailTransiently()/.WillThrow()/.WillHang()`, `InMemoryNotificationRepository`, `TestOptionsMonitor`),
  `FakeTimeProvider`. Pass `TestContext.Current.CancellationToken` to async calls (xUnit1051 is an error).
  Test names: `Method_Scenario_ExpectedResult`.
- Value objects are sealed records with private constructors and `Create(...)` factories that throw `DomainException`.
  **Exception messages must not contain personal data** (email addresses, phone numbers).
- Logging uses `[LoggerMessage]` source-generated static methods.
- Snake_case table/column names configured explicitly in EF configurations.

### Known pitfalls (learned the hard way)

- EF Core does not apply configured complex-type column names when composing over `FromSql` (it looked for `Content_Body`).
  Load entities with LINQ, not raw SQL.
- Testcontainers: use `new PostgreSqlBuilder("postgres:17-alpine")` (the parameterless constructor is obsolete) and
  `Testcontainers.PostgreSql` >= 4.15.0 (older versions pull a vulnerable SSH.NET).
- The generated migration shows an `xmin` column with `rowVersion: true`; Npgsql does not create it (system column). That is expected.

## Status and remaining steps

Done: 0 skeleton, 1 domain model, 2 retry policy, 3 provider selection, 4 dispatcher with failover, 5 send/query use cases with
idempotency, 6 PostgreSQL persistence (+ fix: load claimed notifications via LINQ), 7 providers + startup validation, 8 dispatch worker.

7. **Providers** (`feat(infra)`): simulated `Twilio` and `Vonage` (SMS) and `AmazonSes` (email) sharing a `SimulatedProvider`
   base with configurable behaviour (failure rate, latency, always-fail / permanent-failure modes) so failover can be demoed;
   a real `Smtp` email provider using MailKit, sending to Mailpit. Startup validation (`IValidateOptions` + `ValidateOnStart`):
   configured providers must exist and only list channels they support; priorities valid.
8. **Dispatch worker** (`feat(api)`): `BackgroundService` with `PeriodicTimer`; claims a batch with a lease
   (`WorkerOptions`: polling interval, batch size, lease duration > attempt timeout x providers), dispatches each notification in its
   own DI scope, saves with `UpdateAsync`, logs and skips `ConcurrencyConflictException`; one failing notification must not stop the batch.
9. **REST API** (`feat(api)`): `POST /notifications` (optional `Idempotency-Key` header) -> 202 + `Location` (200 for a duplicate),
   400 `ProblemDetails` for `DomainException`, 409 for idempotency conflicts; `GET /notifications/{id}` -> details with attempts or 404;
   health checks (incl. database); OpenAPI + Scalar UI; DI wiring and `appsettings` provider configuration; apply migrations on startup
   in Development.
10. **End-to-end tests** (`test`): `WebApplicationFactory` + Testcontainers; POST -> worker -> Delivered; failover through the real
    pipeline; retry after all providers fail (fake providers and `FakeTimeProvider` substituted in DI).
11. **README** (`docs`): design decisions, assumptions, trade-offs, what would come next (circuit breaker per provider, outbox,
    messaging adapter, push channel, auth with per-caller idempotency keys, delivery-status webhooks), and the **AI usage** section:
    tools and models used, what they were used for, where AI materially contributed, and what the developer changed or validated
    (e.g. the vulnerable Testcontainers version caught by warnings-as-errors, the `FromSql` complex-type bug caught by integration
    tests, migration code-style exclusion).
