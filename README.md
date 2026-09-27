# Notification Service

A service that accepts requests to notify customers and delivers them over SMS or email
through configurable, prioritised providers with failover and retries.

> Work in progress. Design decisions, assumptions and trade-offs will be documented here.

## Ubiquitous language

| Term | Meaning |
|---|---|
| **Notification** | A request to inform a customer of something through one channel, plus its delivery history. The aggregate root. |
| **Channel** | The communication medium: `Sms` or `Email`. |
| **Recipient** | A channel together with a validated address for it (E.164 phone number or email address). |
| **Content** | What the customer receives: a body and, for email, a subject. |
| **Provider** | An external service that delivers notifications on one or more channels (e.g. Twilio, Amazon SES). |
| **Delivery attempt** | One provider being asked to deliver a notification, and the outcome. |
| **Idempotency key** | A caller-chosen key for one logical send request; repeating the request with the same key returns the original notification. |
| **Dispatch** | One pass over the eligible providers for a notification, in priority order, until one delivers it. |
| **Retry** | A later dispatch, scheduled when a dispatch ended without delivery. |
| **Delivery outcome** | `Delivered`, `TransientFailure` (another provider or a later retry may succeed) or `PermanentFailure` (no provider can deliver it, e.g. the address does not exist). |

## Accepting notifications

Sending is asynchronous: a request is validated, stored as `Pending` and acknowledged immediately; delivery happens in the background.
This keeps callers independent of provider latency and outages, and means an accepted notification survives restarts.

**Idempotency.** Callers retry requests too (timeouts, their own restarts). An optional idempotency key prevents duplicates:

- Same key, same details: the original notification is returned and nothing new is created.
- Same key, different details: the request is rejected as a conflict, since silently returning the earlier notification would hide a caller bug.
- Two concurrent requests with the same key: a unique index decides the winner; the other request returns the winner's notification.
- Assumption: keys are unique across all calling services (e.g. GUIDs or `<service>:<business id>`). With authentication in place, keys would be scoped per caller.

## Dispatching and failover

A **dispatch** tries the eligible providers in priority order and stops at the first one that delivers:

| Provider result | What happens |
|---|---|
| `Delivered` | Notification is `Delivered`; no other provider is called. |
| `TransientFailure` | The next provider is tried. |
| Exception thrown | Treated as a `TransientFailure` (only the exception type is stored; details go to the logs). |
| No response within `DeliveryAttemptTimeout` | Treated as a `TransientFailure`; the call is cancelled. |
| `PermanentFailure` | Notification is `Failed`; no other provider is called. |

If every provider fails transiently, or none is eligible, a retry is scheduled (see below).
Each retry starts again from the highest-priority provider, since the primary may have recovered.
If the service is shutting down mid-dispatch, nothing is scheduled; the notification stays due and is picked up again.

## Retry strategy

- A dispatch that ends without delivery (all eligible providers failed transiently, or none was eligible)
  schedules the next dispatch using exponential backoff with jitter: about 1, 2, 4, 8, 16, 32 and 60 minutes.
- Jitter (±20%) spreads out retries of notifications that failed together during a provider outage.
- After 8 dispatches (about two hours) the notification is marked `Failed`.
- A `PermanentFailure` (e.g. the address does not exist) fails the notification immediately, because retrying cannot help.

## Provider configuration

Providers are configured in the `Notifications:Providers` section, keyed by provider name:

```json
"Notifications": {
  "Providers": {
    "Twilio":    { "Enabled": true, "Priority": 1, "Channels": [ "Sms" ], "Simulation": { "FailureRate": 0.2 } },
    "Vonage":    { "Enabled": true, "Priority": 2, "Channels": [ "Sms" ] },
    "Smtp":      { "Enabled": true, "Priority": 1, "Channels": [ "Email" ] },
    "AmazonSes": { "Enabled": true, "Priority": 2, "Channels": [ "Email" ], "Simulation": { "Mode": "AlwaysFail" } }
  },
  "Smtp": { "Host": "localhost", "Port": 1025, "FromAddress": "notifications@example.com" }
}
```

- A provider is **eligible** for a channel when it is configured, enabled, configured for that channel and technically supports it.
- Eligible providers are tried in ascending `Priority` (1 first); equal priorities are ordered by name.
- A provider implementation without a configuration entry is never used, so adding code alone does not route traffic to it.
- Configuration is read on every dispatch, so disabling or re-prioritising a provider does not need a restart.
- **Validated at startup** (`ValidateOnStart`): an unknown provider name (e.g. a typo), a channel the provider cannot deliver on,
  a priority below 1, an enabled provider without channels, or invalid simulation/SMTP settings stop the application
  with a clear message instead of silently leaving notifications undelivered. Equal priorities are allowed (ordered by name).
  Having no enabled provider for a channel is allowed on purpose: it is how an operator pauses a channel, and notifications wait for retries.

## Providers

| Provider | Channel | Implementation |
|---|---|---|
| `Smtp` | Email | Real: sends through an SMTP server with MailKit. Locally this is Mailpit; open http://localhost:8025 to see sent mail. |
| `AmazonSes` | Email | Simulated |
| `Twilio` | SMS | Simulated |
| `Vonage` | SMS | Simulated |

**Why simulated providers.** Real Twilio, Vonage and SES integrations need accounts and credentials, and add nothing to the design
beyond an HTTP call. The simulated providers implement the same `INotificationProvider` port, so replacing one with a real HTTP client
changes no other code. They share a `SimulatedProvider` base whose behaviour is configured per provider under `Simulation`
(re-read on every attempt, so an outage can be switched on at runtime to demonstrate failover):

| Setting | Effect |
|---|---|
| `Mode: Normal` (default) | Delivers, except a random `FailureRate` share (0 to 1, default 0) of attempts that fail transiently. |
| `Mode: AlwaysFail` | Every attempt fails transiently, like an outage: the next provider is tried, then retries are scheduled. |
| `Mode: PermanentFailure` | Every attempt fails permanently, like a non-existent address: the notification fails immediately. |
| `Latency` (default `00:00:00.1`) | How long each attempt takes. Set it above `DeliveryAttemptTimeout` to simulate a hanging provider. |

**SMTP error classification.** Only a 5xx rejection of the *recipient* (e.g. `550 mailbox unavailable`) is a `PermanentFailure`,
because no provider can deliver to that address. 4xx responses, a rejected sender or message, connection, TLS and authentication
errors are `TransientFailure`s: they are problems with this server or its configuration, so another provider or a later retry may succeed.
The server's response text is logged by status code only, because it often echoes the recipient's address.
Each email carries an `X-Notification-Id` header and its `Message-ID` is stored as the provider message id, for tracing.
A new SMTP connection is opened per message; a connection pool would be the next step at higher volume.

## Persistence

PostgreSQL through EF Core. Two tables: `notifications` (the aggregate, with recipient and content as complex types)
and `delivery_attempts` (an owned collection).

- **Claiming work across instances.** The dispatch worker claims due notifications with a single statement:
  `UPDATE ... WHERE id IN (SELECT ... FOR UPDATE SKIP LOCKED) RETURNING id`.
  `SKIP LOCKED` lets several instances claim different rows at the same time without blocking each other.
- **Leases instead of long transactions.** Claiming sets `locked_until`. The row locks are released immediately,
  so no transaction stays open while providers are called. If an instance crashes, its lease expires and another instance takes over.
- **Optimistic concurrency.** PostgreSQL's `xmin` system column is the concurrency token. If a lease expired and
  another instance processed the notification meanwhile, the slower instance's save fails instead of overwriting.
- **Persistence details stay out of the domain.** `locked_until` and `xmin` are EF shadow properties; the only concession
  in the domain model is a private parameterless constructor for materialization.
- A partial index on `next_attempt_at WHERE status = 'Pending'` keeps polling cheap as delivered notifications accumulate.

### Migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/NotificationService.Infrastructure --startup-project src/NotificationService.Api --output-dir Persistence/Migrations
```

## Running locally

```bash
docker compose up -d          # PostgreSQL + Mailpit (http://localhost:8025)
dotnet build
dotnet test                   # needs Docker: integration tests start PostgreSQL and Mailpit with Testcontainers
dotnet run --project src/NotificationService.Api
```
