# 0005. Each claimed notification is its own unit of work; batches are dispatched concurrently

- Status: Accepted
- Date: 2026-09-27
- Decided by: proposed by Claude while building the dispatch worker, reviewed and accepted by the developer

## Context

The first version of `ClaimDueAsync` returned loaded notifications. Building the worker exposed two problems:

- EF Core can only save an entity through the `DbContext` that loaded it. Saving each notification in its own DI scope (so one
  failed save cannot leave dirty state that breaks the others) was therefore impossible with entities loaded while claiming.
- The lease is sized for one dispatch ([0002](0002-leases-skip-locked-and-optimistic-concurrency.md)). Dispatching a batch one
  notification after another would outlive the lease, and another instance could claim notifications still being worked on.

## Decision

- `ClaimDueAsync` returns **ids only**, earliest due first.
- For each id, `DispatchWorker` creates a DI scope and runs `DispatchNotificationHandler`: load, re-check that it is still due,
  dispatch, save. A failure affects only that notification, which keeps its lease and is retried when the lease expires.
- The claimed batch is dispatched **concurrently** (`Task.WhenAll`), so every notification finishes within its lease.
- If the batch was full, the worker claims again immediately instead of waiting for the next tick, so a backlog drains quickly.

## Alternatives considered

- **One scope for the whole batch, detaching entities after a failed save:** works for sequential processing, but not concurrently,
  and still leaves the lease problem.
- **A lease sized for a whole batch:** a crashed instance would strand its notifications for much longer.

## Consequences

- One extra query per notification (claiming, then loading); negligible compared with provider calls.
- Batch size bounds concurrency, and with it provider and database connections (validated to at most 500).
- This changed an interface introduced in the persistence step; the change and its tests are in the worker step's history.
