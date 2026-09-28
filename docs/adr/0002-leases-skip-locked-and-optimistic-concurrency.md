# 0002. Claim work with leases, SKIP LOCKED and optimistic concurrency

- Status: Accepted
- Date: 2026-09-27
- Decided by: proposed by Claude, reviewed and accepted by the developer

## Context

Several service instances may poll the same database. Each due notification must be dispatched by one instance at a time,
a crashed instance must not strand its notifications, and provider calls (seconds each) must not hold database locks.

## Decision

- **Claim** due notifications with one statement: `UPDATE ... SET locked_until = now + lease WHERE id IN (SELECT ... FOR UPDATE SKIP LOCKED) RETURNING id`.
  `SKIP LOCKED` lets instances claim different rows concurrently without waiting on each other; row locks last only for this statement.
- **The lease** (`locked_until`) reserves a notification while it is dispatched. Saving the outcome clears it; if the instance
  crashes, the lease expires and another instance claims the notification.
- **Optimistic concurrency** with PostgreSQL's `xmin` system column: if a lease expired and another instance saved the notification
  meanwhile, the slower save fails with `ConcurrencyConflictException`, which is logged, and the other result wins.
- `locked_until` and `xmin` are EF Core shadow properties, so the domain model knows nothing about them.
- The lease must outlast the longest possible dispatch (attempt timeout x enabled providers for a channel); startup validation enforces this.

## Alternatives considered

- **A transaction held open during provider calls:** simpler, but holds locks and connections for seconds, and a crash mid-call rolls back
  the record of an attempt the provider may already have delivered.
- **A single worker instance (leader election):** avoids races, but adds a coordination mechanism and a single point of failure.

## Consequences

- **Delivery is at least once:** if an instance crashes after a provider accepted a notification but before saving, the notification
  is dispatched again when the lease expires. Exactly-once would need provider-side idempotency (not offered by most SMS/email APIs).
- Instance clocks must be synchronised to well within the lease duration, since leases compare each instance's clock.
