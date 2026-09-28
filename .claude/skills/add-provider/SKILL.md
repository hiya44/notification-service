---
name: add-provider
description: Add a new notification provider (e.g. a simulated MessageBird for SMS, or a real HTTP-based email provider) to the Notification Service, with registration, configuration, tests and docs. Use when asked to add, integrate or simulate a provider.
argument-hint: <ProviderName> <Sms|Email> [simulated|real]
---

# Add a provider

Arguments: `$ARGUMENTS` — provider name (PascalCase, also its configuration key), channel(s), and whether it is **simulated**
(default: a `SimulatedProvider` subclass, like Twilio) or **real** (calls an external API, like `SmtpEmailProvider`).
If the name or channel is missing, ask before starting.

Read `src/NotificationService.Infrastructure/CLAUDE.md` first: it has the provider rules (error classification, no personal data).

## Checklist

Work through every item; several of them break the build or tests if skipped.

1. **Provider class** in `src/NotificationService.Infrastructure/Providers/`:
   - *Simulated:* `Simulated/<Name>Provider.cs`, a sealed subclass of `SimulatedProvider` with `public const string ProviderName = "<Name>";`.
     Copy `Simulated/VonageProvider.cs` (SMS) or `Simulated/AmazonSesProvider.cs` (email) as the template.
   - *Real:* its own folder (like `Smtp/`) with an options class, and a `SendAsync` that maps every expected result to a `DeliveryOutcome`.
     Only a recipient-level problem is a `PermanentFailure`; outages, throttling, authentication and configuration errors are transient.
     Expected failures are returned, not thrown. Never copy the provider's response text into a failure reason or log (it may contain
     the recipient's address); keep status or error codes. Put the classification in an `internal static` method so it can be unit-tested.
2. **Registration** in `src/NotificationService.Infrastructure/ProviderServiceCollectionExtensions.cs` (`AddNotificationProviders`):
   `AddSimulatedProvider<<Name>Provider>(...)` for a simulated one; for a real one, bind its options with validation and `ValidateOnStart`,
   then `services.AddSingleton<INotificationProvider, <Name>Provider>()`.
3. **Configuration** in `src/NotificationService.Api/appsettings.json` under `Notifications:Providers`: `Enabled`, `Priority` (>= 1;
   decide with the developer where it sits in the failover order), `Channels`. Optionally a `Simulation` block in `appsettings.Development.json` for demos.
4. **Registration test:** add the name to the expected list in
   `tests/NotificationService.Infrastructure.Tests/Providers/ProviderRegistrationTests.cs` (`AddNotificationProviders_RegistersAllProviders`).
5. **End-to-end factory:** add a `TestProvider` with the same name and channel to
   `tests/NotificationService.IntegrationTests/Api/NotificationServiceFactory.cs` (property and the `Providers` list). Without it,
   startup validation rejects the configuration entry from step 3 and every end-to-end test fails.
6. **Provider tests:** a simulated provider is covered by `SimulatedProviderTests`; add a test only for behaviour of its own.
   A real provider needs unit tests of its outcome classification (`tests/NotificationService.Infrastructure.Tests/Providers/`) and,
   if a local test double exists (like Mailpit for SMTP), an integration test in `tests/NotificationService.IntegrationTests/Providers/`.
7. **Lease check:** `WorkerOptionsValidator` requires `LeaseDuration` > `DeliveryAttemptTimeout` x enabled providers per channel.
   With the defaults (2 min, 10 s) that allows 11 providers per channel; if the new provider makes the rule fail, tell the developer.
8. **README:** add a row to the table in the *Providers* section and, if useful, to the configuration example in *Provider configuration*.

## Finish

Build and run the unit tests and the integration tests (`dotnet test`, Docker required: steps 3 and 5 affect the end-to-end tests).
Then report using `/finish-step`.
