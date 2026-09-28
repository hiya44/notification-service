---
name: new-migration
description: Generate and review an EF Core migration after changing the persistence model (NotificationConfiguration or the domain types it maps). Use whenever the database schema needs to change.
argument-hint: <MigrationName in PascalCase, e.g. IncreaseRecipientAddressLength>
---

# New EF Core migration

Arguments: `$ARGUMENTS` — the migration name, describing the change (`AddPushChannel`, `IncreaseRecipientAddressLength`).

Migrations are **generated, never edited by hand**: editing files under `Persistence/Migrations/` is denied in `.claude/settings.json`.
If the generated migration is wrong, fix the model and regenerate.

## Steps

1. Make sure the model change is complete and builds: `dotnet build`.
2. Generate:
   ```bash
   dotnet tool restore
   dotnet ef migrations add <Name> --project src/NotificationService.Infrastructure --startup-project src/NotificationService.Api --output-dir Persistence/Migrations
   ```
3. **Review the generated `<timestamp>_<Name>.cs`** and report what it does:
   - It contains only the intended changes. Unexpected operations mean the model and the snapshot disagree; stop and investigate.
   - **Data safety:** dropped or renamed columns and tables (EF may generate drop plus add for a rename, which loses data), narrowed
     types or lengths, new non-nullable columns without a default (these fail on a table with existing rows).
   - Indexes, especially the unique index on `idempotency_key` and the partial index on `next_attempt_at`, are preserved.
   - Names are snake_case.
   - Expected, not a problem: `xmin` appears with `rowVersion: true` in the snapshot; PostgreSQL provides it as a system column.
4. If it is wrong: `dotnet ef migrations remove --project src/NotificationService.Infrastructure --startup-project src/NotificationService.Api`
   (ask the developer first if the migration may already have been applied anywhere), fix the model, and generate again.
5. Verify: `dotnet build`, then `dotnet test tests/NotificationService.IntegrationTests` (Docker required). The test fixture applies all
   migrations to a fresh PostgreSQL container, so a broken migration fails there.

Report the migration's operations in plain words, any data-safety concerns, and the test results. The developer decides whether it is
safe to apply to existing databases.
