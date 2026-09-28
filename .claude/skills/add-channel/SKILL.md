---
name: add-channel
description: Add a new delivery channel (e.g. Push, WhatsApp) to the Notification Service across domain, persistence, API, providers, tests and docs. Use when asked to support a new way of notifying customers.
argument-hint: <ChannelName> [address type, e.g. "device token"]
---

# Add a channel

Arguments: `$ARGUMENTS` — the channel name (a `Channel` enum value) and what its recipient address is.

A channel touches every layer. **Before writing code, agree with the developer on:** the address format and its validation, content
rules (is a subject allowed or required? maximum body length?), which provider(s) will deliver it, and whether this deserves an ADR
(a channel with different delivery semantics, such as read receipts, probably does). Then work through the checklist.

## Checklist

1. **Domain** (`src/NotificationService.Domain/Notifications/`), following `src/NotificationService.Domain/CLAUDE.md`:
   - Add the value to `Channel.cs` with the next explicit number (values are stored as strings, so numbers only need to be unique).
   - A value object for the address, like `PhoneNumber.cs` or `EmailAddress.cs`: sealed record, private constructor, `Create` that throws
     `DomainException` **without echoing the value**, and a `MaxLength`.
   - `Recipient.cs`: a factory method (like `Recipient.Sms`) and a case in `Recipient.Create`.
   - `Notification.cs`: content rules for the channel in `EnsureContentFitsChannel`.
2. **Persistence:** `recipient_address` is sized by `EmailAddress.MaxLength` in
   `src/NotificationService.Infrastructure/Persistence/Configurations/NotificationConfiguration.cs`. If the new address can be longer,
   change the configuration and generate a migration with `/new-migration`. `channel` is a string column (max 20 characters), so a new
   value needs no migration if its name fits.
3. **API** (`src/NotificationService.Api/Notifications/`): update the "Channel is required: Sms or Email." message in
   `NotificationEndpoints.cs` and the parameter docs of `SendNotificationRequest` in `NotificationContracts.cs`. Add a request to
   `NotificationService.Api.http`.
4. **Providers:** at least one provider must support the channel (`/add-provider`), configured for it in `appsettings.json`.
   Otherwise every notification on the channel waits for retries and then fails with "no eligible provider".
5. **Tests:**
   - Domain: the address value object (valid and invalid values), `RecipientTests`, and the content rules in `NotificationTests`.
   - Application: `ProviderSelectorTests` if selection is affected.
   - End-to-end: a `TestProvider` for the channel in `NotificationServiceFactory`, a sample request helper in `ApiTestBase`, and a
     delivery test in `DeliveryPipelineTests`.
6. **Docs:** README glossary (*Channel*, *Recipient*), the requirements table, the *API* section, the providers table; the root
   `CLAUDE.md` ubiquitous language line; an ADR if agreed above.

## Finish

Run the full `dotnet test` (Docker required), then report using `/finish-step`.
