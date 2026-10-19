# Discord feed integrations

The API supports this frontend flow: **Integrations → Discord Feeds → choose games, regions, and schedule → save → Connect Discord → select server/channel in Discord → connected feed**. Feed services are in `SengokuProvider.Library`; the API contains the controller and hosted worker. This backend repository does not implement the frontend screens.

## Dynamic options

`GET /api/event-feeds/options` returns:

- `games`: IDs and display names read from the existing `public.games` table; new games appear without code changes.
- `regions`: IDs, labels and state membership from `event_feed_regions`. Selected region IDs are stored, and their current membership is resolved during previews and delivery. Editing membership affects existing feeds.
- `schedules`: enabled schedule types, labels, default interval values and whether a UTC time is required, from `event_feed_schedules`.
- `discordConnectionAvailable` and `deliveryEnabled`: configuration readiness.

Region seeds include Southeast, Northeast, Midwest, Pacific Northwest, West, and Southwest (for the remaining southern/western states). Southeast is AL, FL, GA, KY, LA, MS, NC, SC, TN, VA as requested. Other mappings are editable initial product defaults, not an official geographic standard. Reapplying the migration preserves administrator edits. Disable a region to remove it from selection; existing feeds referring to a retired region pause with a failed delivery outcome instead of blocking other feeds.

Game/region selections are validated against the database on save and preview. Structural safety limits remain in code: at most 20 games, 60 regions/states, 10 events per message, and a 90-day lookahead. Schedule *behavior* remains implemented by the application/SQL; adding a new schedule engine requires code, while labels, defaults, availability and region membership are data.

## Requests and responses

All endpoints require the current account session. POST, PUT, DELETE and preview require the existing `X-CSRF-TOKEN` antiforgery header. The OAuth GET callback is protected by a random, single-use state tied to the account, session, feed and feed version.

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/event-feeds/options` | Populate the integration form |
| POST | `/api/event-feeds/preview` | Preview an EventFeedFilter without sending |
| POST | `/api/event-feeds` | Save filters and schedule before connecting Discord |
| GET | `/api/event-feeds` | List the current user's feeds |
| GET | `/api/event-feeds/{id}` | Read configuration, connection status and version |
| PUT | `/api/event-feeds/{id}` | Update using `expectedVersion` |
| DELETE | `/api/event-feeds/{id}` | Remove feed and its delivery history |
| POST | `/api/event-feeds/{id}/discord/connect` | Return Discord `authorizationUrl` |
| GET | `/api/event-feeds/discord/callback` | Exchange `code` and `state`; return connected feed |
| GET | `/api/event-feeds/{id}/deliveries` | Most recent 50 delivery outcomes |

Create request (replace `gameIds` with IDs returned by options):

```json
{
  "name": "Southeast fighting game tournaments",
  "filter": {
    "gameIds": [1],
    "regionIds": ["southeast"],
    "attendance": "InPerson",
    "lookAheadDays": 14,
    "registrationOpenOnly": true,
    "minimumEntrants": 0,
    "maxEvents": 10
  },
  "schedule": { "mode": "Interval", "intervalHours": 6 },
  "enabled": true
}
```

Other schedules:

```json
{ "mode": "Daily", "utcTime": "18:30" }
{ "mode": "OnInsert" }
```

Interval accepts any whole number of hours from 1 to 8760. Daily requires `HH:mm` in UTC and does not drift with local daylight saving changes. Interval schedules begin one interval after connection/save; daily schedules start at the next occurrence of the selected time. Missed scheduled runs produce one current digest rather than a burst of old messages. `schedule` is required. The old `frequencyHours`, `firstDeliveryAt` and `webhookUrl` request fields are replaced by this contract.

A newly saved feed returns `connectionStatus: "AwaitingDiscord"` and cannot deliver, even if enabled. After connecting, it returns `Connected` and the selected guild/channel IDs. No webhook token or URL is returned. For disconnected or OnInsert feeds, `nextRunAtUtc` is null. Feed updates require the latest version; successful Discord connection increments that version too. Updates cancel pending snapshots/notifications and restart the schedule using the new settings.

Advanced filters also support explicit `states` and case-insensitive exact `city`. Attendance is InPerson, Online, or Both. Online events ignore geographic selection; Both combines matching US locations with online events. Online eligibility by location is not inferred. Messages show city/state or Online, signup link, event time, and entrants from the latest local intake; absent entrants are Unknown.

## Why Connect Discord instead of copying a generated token

Discord owns the webhook endpoint and token for its channels. A token generated by our API cannot be pasted into Discord to register a channel. The `webhook.incoming` OAuth scope lets the user authorize the target server/channel, and Discord returns the created webhook to our backend. The backend encrypts it with ASP.NET Data Protection and sends digests to it. The user does not handle a token.

Configure a Discord application with an exact HTTPS redirect URI, then set:

```text
DiscordOAuth:ClientId
DiscordOAuth:ClientSecret
DiscordOAuth:RedirectUri
```

Environment equivalents use double underscores, such as `DiscordOAuth__ClientSecret`. Keep the secret in server configuration/secret storage. A typical callback path is `https://<your-api-host>/api/event-feeds/discord/callback`. The backend callback returns JSON; the integrations frontend should handle completion and refresh the feed. If using a frontend callback route, forward the code/state to the API with the same authenticated session. Never put a webhook token or client secret into the frontend.

Authorization state expires after ten minutes, is consumed before code exchange, and cannot be replayed from another session/account. If the feed was edited during authorization, connection fails rather than silently overwriting newer settings. Known attachment conflicts trigger best-effort cleanup of the newly created, unused webhook; a network failure can leave an unattached webhook in Discord. Reconnecting does not delete previously connected webhooks, and deleting a feed stops our sends but does not remove the Discord integration itself.

## Insert-triggered delivery

Database triggers record newly usable tournaments in a durable notification queue. Both the event metadata and tournament link must exist; missing geography delays capture for in-person events. A candidate queue reconciles concurrent transactions where neither insertion trigger could see the other side yet. Updates to already observed tournaments do not reannounce them, and transaction rollback also rolls back notifications.

Only enabled, connected OnInsert feeds receive newly captured tournaments. Existing catalogue entries are marked observed when the migration is first installed and are not replayed. Pending notifications are filtered at processing time and batched to the requested maximum; additional entries remain queued for subsequent messages. Events outside the selected filters are consumed without a message. The worker polls every 30 seconds, so this is near-real-time delivery after commit, subject to queue volume and Discord rate limits.

Scheduled digests intentionally include all currently matching upcoming events each time; OnInsert digests include only newly captured tournaments. Preview always shows current matches, not the insert queue.

## Deployment

Apply migrations in order before deploying the API or intake worker:

1. `database/migrations/20261013_event_feeds.sql`
2. `database/migrations/20261013_event_feed_functions.sql`
3. `database/migrations/20261013_event_feed_integration_ux.sql`

The third migration requires the existing `games`, `users`, `user_sessions`, `events` and `tournament_links` tables. It preserves earlier feed destinations/settings as Interval feeds. SQL is parameterized and functions use SECURITY INVOKER. Grant the runtime/ingestion roles the necessary table and function permissions, including the notification/candidate tables used by intake triggers; the routines do not elevate privileges. Use a migration role for DDL. Database access uses `ConnectionStrings:AlexandriaConnectionString` from normal appsettings/environment configuration.

Refresh old event records to populate explicit `city`, `addr_state` and `country_code`; broad `regions.province` labels are not state codes. Configure persistent shared Data Protection keys using the existing `Authentication:DataProtectionKeyPath`, with restricted access and key encryption at rest. All API instances need the same key ring/application name.

Set `EventFeeds:DeliveryEnabled=true` only after migration/configuration. Delivery is off by default. The worker locks and durably claims a job before HTTP. Discord 429 delays are honored with up to five attempts. Ambiguous timeouts/server errors/interrupted sends pause a feed as Unknown so an operator can inspect the channel before resuming. Exactly-once delivery across PostgreSQL and Discord is not promised. Terminal jobs scrub payloads and copied destinations; transport logging is disabled and Discord mentions are suppressed.

The legacy unowned subscription route returns 410; old plaintext subscriptions are not imported. Preview broadcast metadata remains available for a future Twitch/YouTube channel guide; this change does not implement that UI or live-status polling.

## Tests

```powershell
dotnet build tests/EventFeeds.Tests/EventFeeds.Tests.csproj -m:1
dotnet run --project tests/EventFeeds.Tests/EventFeeds.Tests.csproj --no-build -- SengokuProvider.API/appsettings.json
```

Tests create/drop a disposable schema using the configured connection, apply migrations twice, and mock all Discord HTTP. They cover editable catalogues, schedules, owner isolation, encryption, HTTP authentication/CSRF, concurrent delivery claims, insert ordering and rollback, batches, retries/recovery, and OAuth session binding/replay. No production schema migration or real Discord message is performed by tests. Without the appsettings path, only non-database checks run.

References: [Discord OAuth2 and incoming webhooks](https://github.com/discord/discord-api-docs/blob/main/developers/topics/oauth2.mdx), [Discord rate limits](https://github.com/discord/discord-api-docs/blob/main/developers/topics/rate-limits.mdx).

## Connect an existing Discord webhook

`POST /api/event-feeds/discord/webhook` accepts an existing incoming channel webhook. It requires the account session cookie and `X-CSRF-TOKEN`, just like other feed writes. OAuth client configuration is not required for this path.

```json
{
  "feedName": "Georgia Strive Feed",
  "webhookUrl": "<your Discord webhook URL>"
}
```

Alternatively, replace `feedName` with `feedId`:

```json
{
  "feedId": "3ee8d4bc-0ee0-4f11-a243-eea51e20a3e7",
  "webhookUrl": "<your Discord webhook URL>",
  "expectedVersion": 2
}
```

Provide exactly one selector. Names are trimmed and matched exactly, ignoring case, within the current user's feeds. Duplicate matching names return 409; use the GUID to disambiguate. Missing or unowned feeds return 404. `expectedVersion` is optional; when supplied it must match the current version. Concurrent edits during verification are rejected even when it is omitted.

The API verifies the webhook with Discord using GET, requires an incoming server/channel webhook, and encrypts the URL using the existing connection store. Only HTTPS discord.com webhook URLs without query parameters or fragments are accepted. Redirects are disabled. Invalid/deleted webhooks return 400; provider/transport failures return a generic 500 without exposing credentials. Success returns 200 with the updated feed, its incremented version, and `Connected` status, never the webhook URL/token.

Attaching replaces the feed's destination but never deletes the old or supplied webhook. It does not send a test message or change the feed's filters, enabled flag, or schedule mode. Delivery still requires the worker and `EventFeeds:DeliveryEnabled`. Use a normal text-channel webhook: forum/media channels require thread parameters that this delivery pipeline does not support. Treat webhook URLs as secrets and keep request bodies out of access logs.
