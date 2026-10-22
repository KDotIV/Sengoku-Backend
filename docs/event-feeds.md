# Shared event feeds and subscriptions

Admins manage the feed catalogue. Signed-in users browse enabled feeds and subscribe; each subscription owns its provider connection, encrypted destination, delivery schedule state, and history.

## Storage and authorization

- `event_feeds`: creator (`user_id`), name, filters, supported providers, schedule, enabled flag, version, and creation time. No webhook or Discord destination fields.
- `event_feed_subscriptions`: GUID, feed/user foreign keys, selected provider, encrypted destination, Discord server/channel IDs, enabled flag, next-run time, and version. Unique `(feed_id,user_id,provider)` makes repeated subscribe requests idempotent.
- Delivery jobs, insert notifications, and OAuth states reference subscriptions. `event_feed_delivery_targets` joins shared definitions with subscriptions for the worker. Filters and schedule definitions have one authoritative copy.
- `user_permissions.id` references `users.id`; `user_type=0` is Admin, matching the existing C# enum. Session authentication loads the role on each request. No username or user ID is hardcoded in authorization. Missing permissions never grant admin access.
- Feed create/update/delete require the Admin role and the SQL functions independently check the same permission. Subscription access always checks its owner. All writes use the store's database try/catch wrapper and parameterized Dapper commands. Version conflicts return 409.

## API

All routes below start with `/api/event-feeds`. All require the account session cookie. POST/PUT/DELETE also require `X-CSRF-TOKEN`; the OAuth GET callback instead validates single-use state and the same session.

| Method | Route | Purpose |
|---|---|---|
| GET | `/options` | Games, regions, schedules, OAuth and worker readiness |
| GET | `/` | Enabled shared feeds (admins also see disabled feeds) |
| GET | `/{feedId}` | Shared definition |
| POST | `/create` | Admin: create shared definition |
| PUT | `/{feedId}` | Admin: edit with `expectedVersion` |
| DELETE | `/{feedId}` | Admin: remove definition and subscriptions |
| POST | `/preview` | Preview matching events; body is the filter object |
| POST | `/{feedId}/subscriptions` | Subscribe; body `{"provider":"Discord"}` |
| GET | `/subscriptions` | Current user's subscriptions |
| GET | `/subscriptions/{subscriptionId}` | Current user's subscription |
| DELETE | `/subscriptions/{subscriptionId}` | Unsubscribe; does not delete the shared feed or remote webhook |
| GET | `/subscriptions/{subscriptionId}/deliveries` | Current user's latest 50 delivery outcomes |
| POST | `/subscriptions/{subscriptionId}/discord/connect` | Begin OAuth for this subscription |
| GET | `/discord/callback` | Finish OAuth and return the connected subscription |
| POST | `/discord/webhook` | Subscribe/connect an existing webhook by feed name or GUID |

Admin create example (use game IDs from options):

```json
{
  "name": "Georgia Strive Feed",
  "providers": ["Discord"],
  "filter": {
    "gameIds": [8681, 37, 48548, 33945],
    "regionIds": ["southeast"],
    "city": "Atlanta",
    "attendance": "InPerson",
    "lookAheadDays": 14,
    "minimumEntrants": 0,
    "registrationOpenOnly": true,
    "maxEvents": 5
  },
  "schedule": { "mode": "OnInsert" },
  "enabled": true
}
```

Other schedules are `{"mode":"Interval","intervalHours":6}` or `{"mode":"Daily","utcTime":"16:05"}` (UTC). OnInsert must omit time fields. Definition edits use the definition version, invalidate stale snapshots/OAuth states, and advance subscription versions; reconnecting a subscription does not change the definition version.

An existing webhook can be attached using:

```json
{
  "feedName": "Georgia Strive Feed",
  "webhookUrl": "<existing Discord webhook URL>"
}
```

Or provide `feedId` instead of `feedName`. Names match exactly ignoring case after trimming, across the shared catalogue; ambiguous names return 409. `expectedVersion`, when supplied here, is the **subscription** version. A new subscription starts at version 1. The endpoint subscribes the current user if necessary and returns the connected subscription, including its `id` and shared `feedId`. It never returns the webhook secret. Connection is verified with GET; after attachment, the existing connection acknowledgement is sent through the Dispatcher webhook handler. Only normal Discord incoming channel webhooks are supported; forum/media channels require thread parameters not supported by this pipeline.

For OAuth, subscribe first, then use the returned subscription `id` in `/subscriptions/{id}/discord/connect`. Open `authorizationUrl` in the same authenticated session. Another user's subscription is never connectable, even for an admin. OAuth states expire after ten minutes and cannot be replayed. Configuration still uses `DiscordOAuth:ClientId`, `ClientSecret`, and `RedirectUri`; the webhook path does not require OAuth configuration.

## Delivery

`EventFeeds:DeliveryEnabled=true` enables the worker at startup; it polls every 30 seconds. Each eligible subscriber gets its own job and encrypted destination snapshot. Concurrent workers lock definitions before subscriptions and claim jobs with `SKIP LOCKED`. Shared feed edits serialize with delivery preparation and reject changes while a send is in progress.

Interval/daily subscriptions send current matching events. OnInsert captures newly usable tournaments for subscribers connected at capture time; it does not replay the existing catalogue or generate empty jobs while idle. Matching rules, limits and message formatting are unchanged. Empty results record `Skipped/NoMatches`. Retryable rate limits are capped at five attempts; ambiguous sends are not automatically replayed. Failure or recovery pauses only the affected subscription, never all subscribers or the shared definition. Reconnect a paused subscription to resume it.

## Migration and rollout

The recovered schema-only snapshots in `database/schema/event-feed-*-before.sql` document the database inspected on 2026-10-20. They contain no account records or destination credentials. They are test fixtures/reference material, not a fresh-install migration for the entire Sengoku database.

1. Stop API/intake workers and back up the database.
2. Apply `database/migrations/20261020_shared_event_feeds.sql` with `psql -v ON_ERROR_STOP=1 -f ...`. It is a one-time transactional migration for the recovered schema. An in-flight `Sending` job blocks migration. Unexpected legacy permission mappings fail their FK validation rather than granting access.
3. Assign administrator permissions explicitly. For the user identified by the project owner:

   ```sql
   INSERT INTO user_permissions(id,user_type)
   SELECT id,0 FROM users WHERE id=491942 AND user_name='Oni_Shogi'
   ON CONFLICT(id) DO UPDATE SET user_type=EXCLUDED.user_type;
   ```

4. Deploy/restart the updated API, retaining the existing Data Protection key ring. Update clients to use subscription IDs for OAuth and history.

Existing feed IDs and definitions are retained. Every old personal feed gets a subscription with the same GUID as its old feed, preserving encrypted destinations, versions and delivery history. Old pending OAuth states are invalidated. No remote webhook is deleted. Existing feed creators retain attribution, but only admins can subsequently edit those shared definitions.

Authentication stays in `SengokuProvider.Library/Configurations` and `Services/Users`. Feed configuration, connection, persistence, delivery models and workers now live in `SengokuDispatcher.Library`. `SengokuProvider.API` retains the frontend feed routes; `SengokuDispatcher.API` is the only delivery/communication host. See the Dispatcher cutover below.

## Verification

```powershell
dotnet build tests/EventFeeds.Tests/EventFeeds.Tests.csproj -m:1
dotnet run --project tests/EventFeeds.Tests/EventFeeds.Tests.csproj --no-build
# Disposable database schema; mocked Discord; cleans up afterward:
dotnet run --project tests/EventFeeds.Tests/EventFeeds.Tests.csproj --no-build -- SengokuProvider.API/appsettings.json
```

Coverage includes migration preservation, admin enforcement and revocation, catalogue visibility, subscription ownership/idempotency, encryption, delivery fan-out, failure isolation, OAuth state ownership/replay, insert fan-out, HTTP authorization/CSRF, and unsubscribe/delete behavior. No real Discord messages are sent.

## Dispatcher ownership and cutover (2026-10-22)

- `SengokuProvider.API` retains **all frontend routes**, including `/api/event-feeds`, `/api/events`, `/api/players`, `/api/leagues`, `/api/exclusights`, account, user, organizer and core endpoints. Existing frontend URLs, session cookies, CSRF protection, request/response shapes and tournament aggregation behavior stay on Provider. No frontend base-URL or reverse-proxy route split is required.
- `SengokuDispatcher.API` exposes only private `/internal/*` communication controls plus `/health/live`, and runs the hosted background jobs. Provider calls these private controls for Service Bus publishing, Discord OAuth/webhook handling and customer communications. Browsers do not call Dispatcher. Every internal request requires a server-only key; Dispatcher does not use browser sessions or frontend CORS.
- `SengokuProvider.Library` remains the shared home for PostgreSQL event/player/legend queries, domain commands, validation and command serialization. Query filters/results remain there. `SengokuDispatcher.Library` owns feed services/models, Service Bus envelopes/publishing/consumers, event/player/legend orchestration, bracket outbox/checkpoints, intake schedules, Discord sockets/webhooks, customer communications, email and report delivery. Both API hosts reference Dispatcher Library, which references Provider Library. Provider executes data workflows and feed CRUD from these libraries but registers remote adapters for communications; it does not instantiate broker clients, sockets, or delivery workers. Provider Library has no reverse Dispatcher reference or Service Bus SDK.
- The API contains HTTP controllers and a hosted-service driver. Business and delivery logic lives in the libraries. The driver owns/disposes each worker scope and propagates background failures to the host. Queue processors stop and drain during shutdown.

### Configuration and rollout

1. Stop the old `SengokuProvider.Worker` WebJobs and the `EventTournamentScheduler` Function App before enabling Dispatcher. Their source entrypoints are retired, but previously deployed binaries must still be stopped. Do not run both consumer sets during cutover.
2. Publish `SengokuDispatcher.API/SengokuDispatcher.API.csproj` to a separate API host. CI produces a `dispatcher-api` artifact; provisioning and deployment of the new host are separate operations. No cloud resources are changed by this refactor.
3. Supply `ConnectionStrings:AlexandriaConnectionString`, `GraphQLSettings:Endpoint`/`Bearer`, and the existing `ServiceBusSettings` connection and three queue names to Dispatcher through environment/application settings. Provider keeps its database/GraphQL settings and the queue names, but needs no broker or Discord credentials. Configure Provider with `Dispatcher:BaseUrl` (private HTTPS URL), `Dispatcher:ApiKey` (at least 32 random characters, identical on both hosts), and `Dispatcher:DiscordOAuthEnabled` (readiness flag). Keep the key server-side, restrict Dispatcher ingress to Provider/internal callers, and rotate the key on both hosts together. Plain HTTP is permitted only for loopback in Development. The old Functions flat settings must be mapped to the new sections. Do not commit credentials. The checked-in Dispatcher settings are a disabled template.
4. Enable `Dispatcher:ServiceBusEnabled` for queue consumption and publishing. Enable `EventFeeds:DeliveryEnabled` for feed polling, independently of Service Bus. Feed jobs remain in PostgreSQL; this move does not convert them to broker messages or change their claim/retry semantics.
5. Enable `Dispatcher:SchedulingEnabled` on **one scheduler instance**. It requires Service Bus. Event intake runs at 01:00/13:00 UTC and at startup; standings run every six hours and at startup; circuit scheduling runs at 02:00/13:00 UTC. This replaces the old five-field timer expressions with explicit UTC times. The timer does not provide distributed schedule deduplication or replay missed runs; keep it disabled on other replicas. Event intake queues directly through the shared workflow instead of calling a hardcoded Provider URL.
6. `Dispatcher:DiscordEnabled` controls the optional ExcluSights socket/customer/email integration; configure `ExclusiveInsightsSettings` and `SendGridSettings` when using it. Feed webhooks/OAuth use their own configuration and do not require the socket integration.
7. Retain the existing Data Protection key ring and set `Authentication:DataProtectionKeyPath` to that shared persistent ring on both APIs, with the existing `Sengoku.Accounts` application name. Provider continues authenticating the user and enforcing CSRF/ownership before delegating Discord operations. Configure the Discord OAuth redirect URI to the existing **Provider** callback URL; Provider forwards the verified user/session context privately to Dispatcher, where existing single-use state and ownership checks still apply. Keep `EventFeeds:DeliveryEnabled` mirrored on Provider for its frontend readiness response; only Dispatcher runs delivery.
8. Deploy Dispatcher first with the private configuration, then Provider with the remote adapters. The Provider publish no longer bundles continuous WebJobs. The old scheduler CI workflow now only builds Dispatcher manually. Private communication requests are not automatically retried: a timeout after an external send is ambiguous. Existing broker/outbox/feed delivery retry policies remain authoritative.

No database schema migration is introduced by this extraction. Existing feed schema, protected destinations, queue payload property names, delivery claims, bracket outbox and checkpoints remain compatible. Apply the previously documented migrations if they are not already installed.

Both APIs emit console logs. Dispatcher adds message IDs/queue names, scheduler lifecycle and PostgreSQL SQLSTATE diagnostics. Feed database and webhook diagnostics omit secrets. PostgreSQL exceptions are caught at the affected data-access/job boundaries and rethrown or translated; a failed database operation is not reported as a successful send.

### Refactor checks

`dotnet build SengokuProvider.API/SengokuProvider.API.sln -m:1` builds both APIs and regression harnesses. `EventFeeds.Tests` also checks Dispatcher dependency scopes, disabled integration startup, Service Bus contract serialization, UTC scheduling and worker failure propagation without contacting a broker. The existing PostgreSQL tests use disposable schemas and mocked Discord; they can still use the Provider settings file for the shared database connection.

### Frontend context reviewed

The requested `C:/Users/fiend/source/repos/Sengoku-FrontEnd` checkout contains only the generated Angular README as Markdown documentation. Its `environment-api.ts`, `event-location.service.ts`, `league.service.ts`, `bracket-runner.service.ts` and `feeds.service.ts` establish a single Provider base URL. That contract is preserved. The checked-out feed service still references legacy `/core` feed endpoints, while this document describes the newer `/api/event-feeds` contract; this refactor does not silently introduce a second frontend origin or rewrite that separate legacy mismatch.

Additional regression checks verify that Provider resolves only remote communication adapters even if background flags are copied into its settings, internal Dispatcher requests reject missing credentials, frontend controllers stay in Provider, and private transport preserves conflict responses without automatically replaying messages.

### Verification recorded for this extraction

- Solution build: passed. The rebuild reported existing nullable initialization warnings in `UserPlayerData.Data` and `PhaseGroup.Sets`; the subsequent incremental build had no warnings.
- EventFeeds suite including isolated PostgreSQL migration/delivery checks and HTTP authorization: passed, with mocked Discord.
- Dispatcher/Provider boundary, private authentication, serializer, schedule and worker lifecycle checks: passed.
- All five regression harnesses passed with PostgreSQL checks enabled: BracketWorkflow, BracketPathQuery, UserOnboarding, AccountAuthentication and EventFeeds. This includes 21 bracket workflow cases and all 10 player-query retry cases.
- Fixed the player-query retry counter so the third failed attempt throws instead of advancing or returning incomplete data. Token rotation still preserves completed pages.
- Restored five missing SQL contracts from the current data-access code (no copies were available in repository history): bracket checkpoints/outbox, matchup keys, user profile, account sessions, and verified registration/bracket metadata. Their migrations are applied twice by the database tests. They are additive baselines for existing domain tables, not a fresh-install schema for the whole application; no deployed database schema was changed by this verification.
- Database coverage includes transactional rollback, concurrent checkpoint/path writes, legacy set-ID preservation, pending outbox commit/cleanup, account and OAuth ownership, session revocation, and public bracket searches. Each harness creates and removes a unique test schema. External providers are mocked.
- UserOnboarding is now a buildable solution project. CI runs all five console harnesses, including retries; CI database checks still require database configuration. `dotnet test` alone does not execute these console harnesses.

Run the complete local verification from the repository root:

```powershell
dotnet build SengokuProvider.API/SengokuProvider.API.sln
./tests/Run-RegressionTests.ps1 -DatabaseSettings SengokuProvider.API/appsettings.json
```

The settings file must contain `ConnectionStrings:AlexandriaConnectionString` for a PostgreSQL database where the test identity can create/drop schemas. The runner does not print credentials and restores connection environment variables afterward. Without `-DatabaseSettings`, the account/feed database checks are explicitly skipped; bracket/onboarding can also use their existing test environment variables.

No live Discord/Service Bus sends or cloud deployments were performed. Before rollout, supply the private endpoint/key, preserve the protection key ring, stop the legacy hosts and enable the desired background flags.

### Final split and deployment review (2026-10-22)

`EventTournamentScheduler.yml` now manually deploys the Dispatcher Web App from `master`, using the existing Provider OIDC secrets. The verified Dispatcher hostname, required Provider settings, OAuth callback, shared key-ring requirement, bot configuration and Azure Always On findings are recorded in [.github/DEPLOYMENT.md](../.github/DEPLOYMENT.md). The final review also fixed array-based GraphQL authentication and moved bearer rotation from embedded credentials to the configured pool. The Azure settings export preserves the user's supplied values and is ignored by Git; see the deployment notes for corrections required before importing it.

Final verification: the solution build and all five harnesses passed with isolated PostgreSQL checks enabled, including 21 workflow cases and 10 retry cases. Dispatcher settings supplied the database connection for this run because Provider's local appsettings JSON currently has a syntax error near StartggOAuth; that local settings file was not modified. No real Discord/Service Bus messages, workflow runs, or Azure mutations were performed.
