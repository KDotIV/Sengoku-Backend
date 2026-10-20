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

Or provide `feedId` instead of `feedName`. Names match exactly ignoring case after trimming, across the shared catalogue; ambiguous names return 409. `expectedVersion`, when supplied here, is the **subscription** version. A new subscription starts at version 1. The endpoint subscribes the current user if necessary and returns the connected subscription, including its `id` and shared `feedId`. It never returns the webhook secret. Connection is verified with GET and does not send a test message. Only normal Discord incoming channel webhooks are supported; forum/media channels require thread parameters not supported by this pipeline.

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

Configuration classes now live in `SengokuProvider.Library/Configurations`. Authentication services live in the existing `Services/Users` namespace. `AddEventFeeds<EventFeedWorker>` registers the API-hosted worker without creating a library-to-API reference.

## Verification

```powershell
dotnet build tests/EventFeeds.Tests/EventFeeds.Tests.csproj -m:1
dotnet run --project tests/EventFeeds.Tests/EventFeeds.Tests.csproj --no-build
# Disposable database schema; mocked Discord; cleans up afterward:
dotnet run --project tests/EventFeeds.Tests/EventFeeds.Tests.csproj --no-build -- SengokuProvider.API/appsettings.json
```

Coverage includes migration preservation, admin enforcement and revocation, catalogue visibility, subscription ownership/idempotency, encryption, delivery fan-out, failure isolation, OAuth state ownership/replay, insert fan-out, HTTP authorization/CSRF, and unsubscribe/delete behavior. No real Discord messages are sent.
