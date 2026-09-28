# 0004. Optional caller-supplied idempotency keys, settled by a unique index

- Status: Accepted
- Date: 2026-09-27
- Decided by: proposed by Claude, reviewed and accepted by the developer

## Context

Callers retry requests after timeouts or their own restarts. Without protection, a retry creates a second notification and the
customer is notified twice.

## Decision

- Callers may send an `Idempotency-Key` header, stored with the notification under a unique index.
- **Same key, same request:** the original notification is returned (`200 OK`) and nothing new is created.
- **Same key, different request:** rejected with `409 Conflict` (`IdempotencyKeyConflictException`), because silently returning
  the earlier notification would hide a caller bug.
- **Concurrent requests with the same key:** the unique index decides; the losing insert (`TryAddAsync` returns false) returns the winner's notification.

## Alternatives considered

- **Mandatory keys:** safer, but a burden for callers that do not retry.
- **Deduplicating on content** (same recipient and body within a time window): guesses intent, and would drop legitimate repeats.
- **Returning the original on a conflicting request** (no 409): hides caller bugs.

## Consequences

- Keys are assumed unique across all callers; with authentication they would be scoped per caller.
- Keys are kept indefinitely, as long as their notifications; a retention policy would bound both.
- Idempotency keys also make the API safe for automated callers, such as AI agents that retry tool calls.
