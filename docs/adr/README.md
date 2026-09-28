# Architecture decision records

Short records of significant decisions: the context, what was decided, the alternatives, and the consequences.
They explain *why* the code is the way it is, for people and for AI assistants working on it (`CLAUDE.md` links here).

| # | Decision | Status |
|---|---|---|
| [0001](0001-asynchronous-delivery-with-database-worker.md) | Asynchronous delivery with a database-backed worker, no message broker | Accepted |
| [0002](0002-leases-skip-locked-and-optimistic-concurrency.md) | Claim work with leases, `SKIP LOCKED` and optimistic concurrency (at-least-once delivery) | Accepted |
| [0003](0003-failover-and-retry-as-domain-rules.md) | Failover and retry are domain rules; retries count dispatches | Accepted |
| [0004](0004-idempotency-keys.md) | Optional caller-supplied idempotency keys, settled by a unique index | Accepted |
| [0005](0005-one-unit-of-work-per-notification.md) | Each claimed notification is its own unit of work; batches are dispatched concurrently | Accepted |

## Adding a record

Copy the structure of an existing record, take the next number, and add it to the table. Never rewrite an accepted record:
when a decision changes, add a new record that supersedes it and set the old one's status to `Superseded by NNNN`.

```markdown
# NNNN. Title in the imperative

- Status: Proposed | Accepted | Superseded by NNNN
- Date: YYYY-MM-DD
- Decided by: who proposed it, who accepted it

## Context
## Decision
## Alternatives considered
## Consequences
```
