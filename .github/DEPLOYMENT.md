# Provider and Dispatcher deployment

`EventTournamentScheduler.yml` now builds/tests the solution, publishes only `SengokuDispatcher.API`, and deploys the artifact to the existing `sengoku-dispatcher` Web App. It is manually triggered; select `master` to deploy. Other branches build/test only. Deployment uses `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID`, matching Provider's OIDC identity. That identity needs deployment access to the new app; being in the same resource group does not establish that an app-scoped role covers both apps. No workflow was triggered during this review.

The Provider workflow continues to deploy `sengoku-alexandria-qa`. Neither package contains the retired scheduler or continuous Provider WebJobs.

## Verified Azure context (2026-10-22)

Both apps exist in resource group `Alexandria` in the selected subscription:

- Provider: `https://sengoku-alexandria-qa.azurewebsites.net`
- Dispatcher: `https://sengoku-dispatcher-fvdfhpcrcdd3afag.eastus-01.azurewebsites.net`

Dispatcher currently reports Always On disabled and Windows runtime configuration `v9.0`. Enable Always On and configure/verify .NET 8 hosting for these net8.0 projects before cutover. No cloud configuration was changed.

## Required configuration before cutover

- Provider: set `Dispatcher__BaseUrl` to the exact Dispatcher URL above, `Dispatcher__ApiKey` to the same independent service key configured on Dispatcher, and `Dispatcher__DiscordOAuthEnabled=true` to expose Discord OAuth connection support. The browser continues to use Provider.
- Dispatcher: set `DiscordOAuth__RedirectUri=https://sengoku-alexandria-qa.azurewebsites.net/api/event-feeds/discord/callback` and register this exact URI in the Discord application. The localhost value in the supplied settings is for local development.
- Both applications must use the same persistent Data Protection key ring and `Sengoku.Accounts` application name. A blank `Authentication__DataProtectionKeyPath` gives separate app-local keys. Matching path strings on separate app-local disks do not share keys; configure a genuinely shared mounted store and preserve the existing keys.
- `Dispatcher__DiscordEnabled` enables the ExclusiveInsights socket bot, not EventFeeds webhook delivery. Set it to false unless `ExclusiveInsightsSettings__CustomerPoolConnectionString`, `ExclusiveInsightsSettings__DiscordBotToken`, and the reporting settings (`SendGridSettings__ApiKey`, `SendGridSettings__FromEmail`) are supplied. EventFeeds delivery and OAuth still work independently when this flag is false.
- `EventFeeds__DeliveryEnabled=true` starts feed processing. `Dispatcher__ServiceBusEnabled=true` starts broker consumption/publishing; `Dispatcher__SchedulingEnabled=true` starts intake scheduling and requires Service Bus. Stop all legacy deployed workers/schedulers before enabling the replacements.
- Keep exactly one scheduling instance enabled. `IntakeScheduleRunner` prevents overlap only within its process and runs two jobs on startup; multiple replicas would each schedule the same work. Distributed scheduling coordination is not implemented by this extraction.
- The supplied `Dispatcher__ApiKey` has the format of a Discord bot token. Use a distinct randomly generated service key shared only by Provider and Dispatcher; bot credentials belong in their bot setting.

The ignored local file `local-settings/sengoku-dispatcher.azure-settings.json` is an exact flattening of the supplied settings for Azure Portal Environment variables / App settings / Advanced edit. It preserves the localhost callback, blank key path, and enabled bot flag, so apply the corrections above before importing it. All values are strings, hierarchy uses double underscores, and array keys end in `__0` through `__3`. Keep this credential-bearing file out of commits and build artifacts.

## Boundary review

Provider retains frontend controllers, cookie authentication/CSRF, feed management, tournament queries and aggregation/intake orchestration. It resolves remote adapters for Service Bus publishing, Discord OAuth/webhook validation, Discord notifications and ExclusiveInsights. Sharing orchestration implementations from Dispatcher.Library does not register background workers in Provider.

Dispatcher owns Service Bus clients and event/player/legend consumers, bracket outbox publishing, scheduled intake, EventFeeds delivery, Discord socket listeners, Discord HTTP calls and reporting integrations. Its API controllers expose only authenticated `/internal` operations; `/health/live` is the public liveness endpoint. The two transport SDKs are referenced only by Dispatcher.Library.

The final review corrected shared GraphQL authentication for array configuration and removed embedded token-rotation credentials in favor of configured tokens. Regression coverage checks the configured token, Provider remote adapters, private endpoint authentication, frontend controller ownership, worker propagation and full data workflows.
