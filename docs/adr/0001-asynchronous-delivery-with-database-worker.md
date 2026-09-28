# 0001. Deliver asynchronously with a database-backed worker

- Status: Accepted
- Date: 2026-09-25
- Decided by: proposed by Claude; the developer chose "REST API only" over "REST plus a messaging consumer"

## Context

Callers (other services) ask for customers to be notified. Providers are slow, rate-limited and sometimes down, and delivery
may need retries over hours. Callers should not wait for providers or fail because of them, and an accepted notification
must not be lost on a restart.

## Decision

`POST /notifications` only validates and stores the notification as `Pending`, and returns `202 Accepted`. A background worker
in the same process polls PostgreSQL for due notifications and delivers them. The database is the queue.

## Alternatives considered

- **Synchronous delivery in the request:** simplest, but the caller waits for providers and retries would block requests.
- **A message broker (RabbitMQ, Azure Service Bus, ...):** reacts immediately and scales consumers independently, but adds
  infrastructure, and the notification's state (attempts, next retry) would still need a database.
- **PostgreSQL `LISTEN/NOTIFY`:** removes polling latency, but adds connection handling; polling remains needed for retries anyway.

## Consequences

- Callers follow progress with `GET /notifications/{id}`; delivery takes up to one polling interval (5 s by default) to start.
- One small indexed query per polling interval, served by a partial index on `next_attempt_at WHERE status = 'Pending'`.
- The application layer is transport-agnostic, so a broker consumer could be added later as another way to accept notifications.
- Multiple instances need coordination when claiming work: see [0002](0002-leases-skip-locked-and-optimistic-concurrency.md).
