# Provider and Dispatcher QA deployment

`master_sengoku-dispatcher.yml` deploys Dispatcher; `sengoku-alexandria-qa.yml` deploys Provider. Both build/test branch pushes; only `master` deploys. Deploy both from the same revision. Existing OIDC identities are retained. Deployment is serialized per app and depends on the private local integration job plus its build job.

## Readiness gates

Before deployment, `.github/scripts/Test-DeploymentReadiness.ps1` verifies .NET 8 hosting. Dispatcher also requires Always On, delivery/outbox/Service Bus flags, database/broker connections and internal authentication. Settings and keys are not logged.

After deployment it waits up to six minutes for the expected source revision:

- Dispatcher `GET /internal/health/ready` requires `X-Dispatcher-Key`. It checks database/schema contracts, destination decryptability, recent successful feed/outbox cycles, paused subscriptions with failed/unknown last sends, and non-consuming queue peeks.
- Provider `GET /health/ready` checks its session schema and Dispatcher's authenticated readiness. Both must have the same source revision. Its public response contains only status and revision.
- `/health/live` checks process liveness only; it is not the deployment success criterion.

Checks do not send notifications, claim jobs or replay failures. Normal App Service deployment performs its application restart. An unconditional shutdown cannot recover a missing key and adds downtime, so it is not used as a repair mechanism. Failed readiness fails the job for investigation without automatically reconnecting, replaying or rolling back the database.

The two workflows may overlap. Provider waits for the matching Dispatcher revision. If Dispatcher takes longer than the deadline, rerun Provider verification when Dispatcher is healthy. Deploying just one app from a different revision intentionally leaves Provider readiness unready.

## Verified hosting and recovery (November 11, 2026)

Resource group: `Alexandria`.

| Service | URL |
| --- | --- |
| Provider | `https://sengoku-alexandria-qa.azurewebsites.net` |
| Dispatcher | `https://sengoku-dispatcher-fvdfhpcrcdd3afag.eastus-01.azurewebsites.net` |

Dispatcher has .NET 8 hosting, Always On and one worker. `SecretUnavailable` was caused by a destination protected with a local-only key. Reconnecting through Azure repaired it; a subsequent delivery was confirmed `Sent` at 22:21:09 UTC on November 11. Recovery is live. Readiness endpoints and workflow gates require deployment of these source changes.

## Data Protection and authentication

Keep Data Protection and application name `Sengoku.Accounts`. Session tokens remain random, hashed database-backed values. Changing key storage does not require removing session authentication or CSRF. Antiforgery and encrypted destinations still require persistent keys.

`Authentication__DataProtectionKeyPath` is an optional override. App Service's default persistent location is `%HOME%\ASP.NET\DataProtection-Keys`. Dispatcher's explicit path already points there, so it can remain without another storage service. Preserve its existing files. Microsoft documents these [persistence defaults](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings?view=aspnetcore-8.0).

Provider authenticates/enforces CSRF and delegates Discord connections to Dispatcher, which alone encrypts/decrypts destinations. These separate applications do not need a shared ring for this flow. Instances of the same application must retain access to that application's keys. Local testing uses separate keys and a separate database. Moving a destination between rings requires the original plaintext connection or preservation of its original keys; restarting cannot recreate a missing key.

## Required configuration

- Provider: set `Dispatcher__BaseUrl` to Azure Dispatcher and `Dispatcher__ApiKey` to its independent internal key. `Dispatcher__DiscordOAuthEnabled` controls frontend OAuth availability. Restrict Dispatcher ingress to intended callers where hosting permits.
- Dispatcher: enable `EventFeeds__DeliveryEnabled`, `EventFeeds__CommsDeliverablesEnabled`, `Dispatcher__ServiceBusEnabled`; supply the Alexandria database, intake queue connection/names and `ServiceBusSettings__CommsDeliverablesConnection` for `commsdeliverablesqueue`.
- Scheduling requires Service Bus. Keep one scheduling instance: coordination is process-local.
- `Dispatcher__DiscordEnabled` controls the socket bot, not feed webhook delivery. Enable only with its bot/customer-pool/reporting configuration.
- Register the Provider callback URI with Discord for OAuth. Keep bot credentials separate from the service authentication key.
- No local Dispatcher may use `alexandia-qa.postgres.database.azure.com` database `qa`. The startup guard requires the QA App Service identity; it prevents configuration mistakes, not malicious credential use.

The trusted integration job uses `PRIVATE_DATABASE_FIXTURES_B64` and temporary PostgreSQL. It does not run QA migrations. See [local testing](../docs/local-testing.md) for fixture maintenance and transport limits. Public clones deliberately exclude the database definitions.
