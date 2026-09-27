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
| **Dispatch** | One pass over the eligible providers for a notification, in priority order, until one delivers it. |
| **Retry** | A later dispatch, scheduled when a dispatch ended without delivery. |
| **Delivery outcome** | `Delivered`, `TransientFailure` (another provider or a later retry may succeed) or `PermanentFailure` (no provider can deliver it, e.g. the address does not exist). |

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
    "Twilio":    { "Enabled": true, "Priority": 1, "Channels": [ "Sms" ] },
    "Vonage":    { "Enabled": true, "Priority": 2, "Channels": [ "Sms" ] }
  }
}
```

- A provider is **eligible** for a channel when it is configured, enabled, configured for that channel and technically supports it.
- Eligible providers are tried in ascending `Priority` (1 first); equal priorities are ordered by name.
- A provider implementation without a configuration entry is never used, so adding code alone does not route traffic to it.
- Configuration is read on every dispatch, so disabling or re-prioritising a provider does not need a restart.

## Running locally

```bash
docker compose up -d          # PostgreSQL + Mailpit (http://localhost:8025)
dotnet build
dotnet test
dotnet run --project src/NotificationService.Api
```
