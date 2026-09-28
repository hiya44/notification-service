# Domain layer

The heart of the model. No dependencies on other projects or packages, no I/O, no clock, no logging.

- **`Notification` is the aggregate root.** State changes only through `RecordDeliveryAttempt(provider, outcome, at)` and
  `ScheduleRetryOrFail(policy, reason, now)`; add behaviour as methods that keep its invariants, never public setters.
- **Time is a parameter** (`DateTimeOffset now`), never `DateTime.UtcNow` or `TimeProvider`.
- **Value objects** are sealed records with private constructors and `Create(...)` factories that throw `DomainException`.
  Once created they are valid; callers never re-validate.
- **`DomainException` messages are returned to API callers** (as 400 problem details), so they must be safe to show and must
  **never contain personal data**: say "Phone number must be in E.164 format", never echo the number.
- **`DeliveryOutcome`:** `TransientFailure` means another provider or a later retry may succeed; `PermanentFailure` means no provider
  can (a recipient-level problem) and fails the notification immediately.
- **`RetryPolicy`:** `MaxDispatches` includes the first dispatch; retries count failed dispatches, not provider calls.
- EF Core needs a private parameterless constructor on the aggregate; that is the only persistence concession allowed here.
  Persistence-only state (leases, concurrency tokens) stays out of the domain as EF shadow properties.

Tests: `tests/NotificationService.Domain.Tests`, pure unit tests with fixed times (`TestNotifications.Now`).
