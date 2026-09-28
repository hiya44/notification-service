# CLAUDE.md

Guide for AI assistants (and people) working on this repository: a **Notification Service** that accepts requests to notify
customers and delivers them over SMS or email through configurable, prioritised providers, with failover and retries.
.NET 10, ASP.NET Core minimal APIs, EF Core + PostgreSQL, hexagonal architecture with a DDD domain model.

- The **README** explains the design, API, trade-offs and how the project was built with AI.
- **`docs/adr/`** records why the main decisions were made. Read the relevant record before changing that area.
- Each layer has its own `CLAUDE.md` with rules and pitfalls for that layer (loaded when you work there).

## How we work

- **The developer reviews and commits every change.** Never commit, push, or discard changes (`reset`, `restore`, `checkout --`,
  `stash`, `clean`); these are denied in `.claude/settings.json`. End a piece of work with a summary, the design decisions worth
  knowing, and a suggested commit message (conventional commits: `feat(api): ...`, `fix(infra): ...`, `test: ...`, `docs: ...`).
- **Small, focused changes**, each leaving the build and tests green.
- **Verify before handing over.** The Stop hook builds and runs the unit tests automatically. When you change persistence,
  the worker, providers or the API, also run `dotnet test tests/NotificationService.IntegrationTests` (needs Docker).
- **Keep documentation current in the same change:** the README for behaviour and configuration, a new ADR for a new
  significant decision (never rewrite an accepted one; supersede it).
- **Ask** when a change would alter a decision recorded in an ADR, the public API contract, or the database schema.

## Guardrails (enforced, not just described)

| What | How |
|---|---|
| No commits, pushes or discarded changes | `permissions.deny` in `.claude/settings.json` |
| EF migrations are generated, never hand-edited | `Edit(**/Persistence/Migrations/**)` denied; use `dotnet ef migrations add` |
| No personal data in exception messages or log templates (production code) | PreToolUse hook `.claude/hooks/guard-personal-data.ps1` |
| Consistent whitespace in edited C# files | PostToolUse hook `.claude/hooks/format-csharp.ps1` |
| Build and unit tests pass before a turn ends | Stop hook `.claude/hooks/verify.ps1` (skips when code has not changed) |
| Warnings (including code style and vulnerable packages) fail the build | `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, NuGet audit |

The hooks are PowerShell scripts run through bash (`pwsh` if installed, otherwise Windows PowerShell), so they work on Windows
with Git Bash, macOS and Linux. `/hooks` shows them; personal overrides go in `.claude/settings.local.json` (not committed).

## Commands

```bash
docker compose up -d        # PostgreSQL 17 (localhost:5432) + Mailpit (SMTP localhost:1025, UI http://localhost:8025)
dotnet build
dotnet test                 # integration tests start their own PostgreSQL and Mailpit via Testcontainers (Docker required)
dotnet run --project src/NotificationService.Api     # http://localhost:5080, Scalar UI at /scalar (Development)
dotnet tool restore
dotnet ef migrations add <Name> --project src/NotificationService.Infrastructure --startup-project src/NotificationService.Api --output-dir Persistence/Migrations
```

`src/NotificationService.Api/NotificationService.Api.http` has demo requests for every endpoint and error case.

## Architecture

Dependencies point inwards: Domain <- Application <- Infrastructure, Api.

| Project | Contains |
|---|---|
| `src/NotificationService.Domain` | `Notification` aggregate, value objects, `RetryPolicy`, `INotificationRepository`. No dependencies. |
| `src/NotificationService.Application` | Use cases (send, get, dispatch one), `INotificationProvider` port, `ProviderSelector`, `NotificationDispatcher`, options and validators |
| `src/NotificationService.Infrastructure` | EF Core repository, simulated providers (Twilio, Vonage, AmazonSes), SMTP provider (MailKit), DI registration |
| `src/NotificationService.Api` | Endpoints, `ApiExceptionHandler`, background `DispatchWorker`, composition root (`Program.cs`) |
| `tests/*.Domain.Tests`, `*.Application.Tests`, `*.Infrastructure.Tests` | Unit tests, no Docker |
| `tests/NotificationService.IntegrationTests` | PostgreSQL and Mailpit via Testcontainers, worker, end-to-end tests (`Api/`) |

Flow: `POST /notifications` -> validated and stored as `Pending` (202) -> `DispatchWorker` claims due notifications with a lease
-> `NotificationDispatcher` tries eligible providers in priority order -> `Delivered`, `Failed`, or a retry scheduled with backoff.

## Core rules

- **The domain is pure:** no I/O, no clock. Methods take `DateTimeOffset now`; everything else uses `TimeProvider`, so tests use `FakeTimeProvider`.
- **The aggregate protects its invariants.** A `Notification` changes only through its methods; value objects cannot be created invalid.
- **Providers never see the aggregate:** they receive a `DeliveryRequest` and return a `DeliveryOutcome`. Only the dispatcher changes notifications.
- **Failover and retry are domain rules** ([ADR 0003](docs/adr/0003-failover-and-retry-as-domain-rules.md)), not resilience-library policies.
- **Delivery is at least once** ([ADR 0002](docs/adr/0002-leases-skip-locked-and-optimistic-concurrency.md)); do not add logic that assumes exactly once.
- **No personal data** (addresses, phone numbers, message content) in exception messages, logs, or provider failure reasons.
  Identify notifications by `NotificationId`.
- **Provider configuration is read on every dispatch** (`IOptionsMonitor`) and validated at startup (`ValidateOnStart`).

## Ubiquitous language

Use these names in code, tests and docs: **Notification**, **Channel** (`Sms`, `Email`), **Recipient**, **Content**, **Provider**,
**Eligible provider**, **Delivery attempt**, **Delivery outcome** (`Delivered`, `TransientFailure`, `PermanentFailure`),
**Dispatch** (one pass over the eligible providers), **Retry** (a later dispatch), **Idempotency key**.
Definitions are in the README.

## Conventions

- .NET 10, C# latest, nullable enabled, **warnings are errors**, code style enforced in the build (file-scoped namespaces, readonly fields).
- **Central package management:** versions only in `Directory.Packages.props`. Vulnerable packages fail the build; pin a patched
  version (see the comment on `Microsoft.OpenApi`) rather than suppressing the warning.
- Logging with `[LoggerMessage]` source-generated methods.
- Match the surrounding code: comment density, naming, and idioms. Comments explain *why*, not what.

## Common changes

- **Add a provider:** implement `INotificationProvider` in Infrastructure (a subclass of `SimulatedProvider` for a simulated one),
  register it in `AddNotificationProviders`, add its entry to `appsettings.json`, add tests, and add it to the README's provider table.
  Classify its errors: only a recipient-level problem is a `PermanentFailure`.
- **Add a channel:** a `Channel` value, recipient validation in `Recipient.Create`, content rules in `Notification`, API contract,
  providers that support it, tests at every level, README (glossary, API, providers).
- **Change the schema:** change the EF configuration, then generate a migration with the command above and review it.
