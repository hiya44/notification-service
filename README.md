# Notification Service

A service that accepts requests to notify customers and delivers them over SMS or email
through configurable, prioritised providers with failover and retries.

> Work in progress. Design decisions, assumptions and trade-offs will be documented here.

## Running locally

```bash
docker compose up -d          # PostgreSQL + Mailpit (http://localhost:8025)
dotnet build
dotnet test
dotnet run --project src/NotificationService.Api
```
