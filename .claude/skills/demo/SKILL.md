---
name: demo
description: Start the Notification Service locally and run the demo scenarios (delivery, failover, idempotency, validation errors, health) against it, reporting the results. Use when asked to demo the service or check it end to end by hand.
disable-model-invocation: true
---

# Demo the service

Runs the scenarios from `src/NotificationService.Api/NotificationService.Api.http` against a locally running instance and reports the results.

## 1. Start

1. `docker compose up -d`, then `docker compose ps` until `postgres` and `mailpit` are healthy.
2. Check whether port 5080 is in use. If the API is already running (for example from Visual Studio), use that instance and do not start another.
3. Otherwise start the API in the background: `dotnet run --project src/NotificationService.Api --launch-profile http`, writing its output
   to a log file in the scratchpad or temp directory, not the repository.
4. Poll `http://localhost:5080/health/ready` until it returns `Healthy` (up to a minute). If it does not, show the end of the log and stop.

## 2. Run the scenarios

Use `curl` against `http://localhost:5080`. Development settings make Twilio fail about 30% of attempts and poll every 2 seconds.

| Scenario | Request | Expected |
|---|---|---|
| SMS delivery and failover | `POST /notifications` SMS five times, wait ~5 s, `GET` each | All `Delivered`; some show Twilio `TransientFailure` then Vonage `Delivered` |
| Email via SMTP | `POST` an email, wait, `GET` it | `Delivered` by `Smtp`; the message is in Mailpit (`GET http://localhost:8025/api/v1/messages`) |
| Idempotency | `POST` with a **new** `Idempotency-Key` (include a timestamp), repeat it, then change the body | 202, then 200 with the same id, then 409 |
| Validation | Invalid phone number; email without subject; missing channel; `"channel": "Pigeon"` | 400 problem details each; the last names `$.channel` |
| Not found | `GET /notifications/<random guid>` | 404 |
| Health | `GET /health/live`, `GET /health/ready` | `Healthy` |

## 3. Report and clean up

- A table of scenario, expected, actual, and pass or fail, plus one example of a failover delivery history.
- Scan the API log (if you started it) for `fail:` or `crit:` entries, and for email addresses or phone numbers that should not be logged.
- Stop the API you started (never one the developer started). Leave the Docker containers running and say so;
  `docker compose down -v` resets the database.
