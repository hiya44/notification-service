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
| **Delivery outcome** | `Delivered`, `TransientFailure` (another provider or a later retry may succeed) or `PermanentFailure` (no provider can deliver it, e.g. the address does not exist). |

## Running locally

```bash
docker compose up -d          # PostgreSQL + Mailpit (http://localhost:8025)
dotnet build
dotnet test
dotnet run --project src/NotificationService.Api
```
