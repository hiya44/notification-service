# Infrastructure layer

EF Core + PostgreSQL persistence and the provider implementations.

## Persistence

- **Snake_case names are configured explicitly** in `Persistence/Configurations`. Recipient and content are EF complex types;
  delivery attempts are an owned collection.
- **`locked_until` (lease) and `xmin` (concurrency token) are shadow properties.** See [ADR 0002](../../docs/adr/0002-leases-skip-locked-and-optimistic-concurrency.md).
- **`ClaimDueAsync` returns ids only** ([ADR 0005](../../docs/adr/0005-one-unit-of-work-per-notification.md)): one
  `UPDATE ... WHERE id IN (SELECT ... FOR UPDATE SKIP LOCKED) RETURNING id` statement, wrapped in a CTE to order earliest-first.
- Repository methods save immediately; there is no separate unit of work. `UpdateAsync` clears the lease and turns
  `DbUpdateConcurrencyException` into `ConcurrencyConflictException`.
- **Migrations are generated, never edited by hand** (edits are denied in `.claude/settings.json`). Change the configuration,
  run `dotnet ef migrations add`, then review the generated file.

### Pitfalls

- **Load entities with LINQ, not raw SQL.** EF Core did not apply configured complex-type column names when composing over
  `FromSql` (it looked for a `Content_Body` column). Raw SQL is fine for statements that return scalars, like the claim.
- **The generated migration lists an `xmin` column with `rowVersion: true`.** That is expected: PostgreSQL has `xmin` as a system
  column, and no real column is created (confirmed in `pg_attribute`).
- **Generated migrations fail the code style build** unless excluded; `.editorconfig` marks `**/Migrations/*.cs` as generated code.

## Providers

- **Every provider implements `INotificationProvider`** and maps its results to `DeliveryOutcome`. Only a recipient-level problem is
  a `PermanentFailure` (for SMTP: a 5xx `RecipientNotAccepted`); outages, throttling, configuration and authentication problems are transient.
- **Expected failures are returned as outcomes, not thrown.** The dispatcher treats unexpected exceptions and timeouts as transient failures anyway.
- **Never put provider responses into failure reasons or logs verbatim;** they often echo the recipient's address. Keep status codes.
- **Simulated providers** subclass `SimulatedProvider`; their behaviour is named options bound from
  `Notifications:Providers:{Name}:Simulation`, re-read on every attempt.
- **Register new providers** in `ProviderServiceCollectionExtensions.AddNotificationProviders`, as singletons, with `ValidateOnStart` on their options.
  `NotificationOptionsValidator` then rejects configuration that names unknown providers or unsupported channels.

Tests: `tests/NotificationService.Infrastructure.Tests` (no Docker), and `tests/NotificationService.IntegrationTests/Persistence`
and `/Providers` (PostgreSQL and Mailpit containers).
