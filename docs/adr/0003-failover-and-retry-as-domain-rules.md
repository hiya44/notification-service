# 0003. Failover and retry are domain rules; retries count dispatches

- Status: Accepted
- Date: 2026-09-27
- Decided by: proposed by Claude, reviewed and accepted by the developer

## Context

The task requires failover to the next provider by priority, and retrying later when no provider can deliver. These could be
implemented with a resilience library (Polly) around provider calls, or modelled explicitly.

## Decision

- **A dispatch** is one pass over the eligible providers in priority order, stopping at the first that delivers.
  `NotificationDispatcher` implements it; `ProviderSelector` decides eligibility and order from configuration.
- **Each provider result is a `DeliveryOutcome`:** `Delivered`; `TransientFailure` (outage, throttling, timeout, unexpected exception:
  try the next provider, then retry later); or `PermanentFailure` (a recipient-level problem such as a non-existent address:
  fail immediately, since no provider can succeed).
- **A retry** is a later dispatch, scheduled by `RetryPolicy` (exponential backoff with ±20% jitter, capped) through
  `Notification.ScheduleRetryOrFail`. The limit counts failed **dispatches**, not provider calls. "No eligible provider" also schedules a retry.
- Each retry starts again from the highest-priority provider, because the primary may have recovered.

## Alternatives considered

- **Polly retry and fallback policies:** well-tested mechanics, but the business rules (what is permanent, when to give up,
  retrying across process restarts over hours) would be spread across policy configuration instead of the domain model,
  and in-memory retries do not survive restarts.
- **Counting provider calls:** makes the retry limit depend on how many providers are configured.

## Consequences

- The rules are unit-tested in the domain and application layers without infrastructure (`RetryPolicyTests`, `NotificationDispatcherTests`).
- "Give up after about two hours" holds regardless of the number of providers.
- A provider that is down still costs a timeout on every dispatch; a circuit breaker per provider is the planned improvement.
