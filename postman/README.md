# Sengoku Postman collection

Import `Core.postman_collection.json`, `Local.postman_environment.json` and `QA.postman_environment.json`. Use one collection and select the environment per run. All 42 requests have descriptions; original examples were grouped and corrected against current controllers. No real password, webhook token, API key or session cookie is included.

## Sessions and sequential requests

Keep Postman's cookie jar enabled. `00 Authentication` gets the antiforgery cookie, logs in and verifies the session. The server sets Secure, HttpOnly `__Host-sengoku-session`; Postman sends it on later requests to that host. No Bearer token needs copying. Protected writes automatically fetch a fresh CSRF request token, including after login changes identity. Passwords containing quotes/backslashes are escaped as JSON.

Run requests sequentially. Enable **Keep variable values** and **Save cookies after collection run** in the Runner to continue manually afterward. Login, CSRF acquisition and subsequent requests must use the same hostname: do not mix `localhost` and `127.0.0.1`. Local and QA cookies are separated by host. Leave Cleanup unselected to retain the session; Logout revokes it. A 401 requires login again. Mutations are never automatically replayed after errors.

Public IDs and concurrency versions are saved in the selected environment. Session cookies/CSRF tokens are not copied into exported variables. Keep real credentials/webhooks in private environment values. Marking a variable sensitive masks its display but does not make exports safe to share; clear secrets before export. See Postman's [cookie management](https://learning.postman.com/docs/use/send-requests/response-data/cookies) and [collection run persistence](https://learning.postman.com/docs/tests-and-scripts/running-collections/intro-to-collection-runs).

## Local workflow

1. Start `pwsh ./scripts/Start-LocalEnvironment.ps1` from the repository root. See [local setup and private fixtures](../docs/local-testing.md).
2. Select **Sengoku Local**. Synthetic credentials, game, tournament and captured webhook are prefilled.
3. Disable SSL verification for local requests using the temporary self-signed certificate. Keep the cookie jar enabled.
4. Select Authentication, Health, Feed workflow and optionally Feed inspection, in that order. Optional league/bracket examples need extra data; Cleanup logs out.
5. Inspect Delivery history and `GET https://localhost:7028/_local/messages`. The wait request polls up to 75 seconds and asserts a new `Sent` delivery.

Each launcher run has a fresh database. Repeated runs within one instance leave feeds available for inspection. To remove a test feed, set `allowDeletes=true` and run explicit cleanup for its saved IDs. Delivery polls approximately every 30 seconds; existing subscriptions may have a one-hour cooldown.

CLI alternative while the local server runs:

```powershell
npx --yes newman@6 run ./postman/Core.postman_collection.json `
  -e ./postman/Local.postman_environment.json `
  --folder '00 Authentication' --folder '05 Health' --folder '10 Feed workflow' `
  --insecure --timeout-script 90000
```

`--insecure` is only for the temporary localhost certificate. Newman preserves cookies/variables within a run; each new process starts a new session. Login in each independent run. Avoid exporting live cookie jars into the repository.

## QA inspection and intentional writes

Select **Sengoku QA**, keep TLS verification enabled and fill `email`/`password` privately. Run Authentication, then Feed inspection. The QA template includes the recovered feed/subscription IDs; subscription access still requires their owning account. Admins cannot impersonate another subscription owner.

QA writes, messages, deletions and manual integrations default off:

| Variable | Effect | Local / QA |
| --- | --- | --- |
| `allowWrites` | Enables mutations except login/logout; read-only POST queries/previews remain usable | true / false |
| `allowExternalMessages` | Enables connection acknowledgements and leaderboard sends; also requires writes | true / false |
| `allowDeletes` | Enables DELETE; also requires writes | false / false |
| `allowManualIntegrations` | Enables OAuth/socket examples; writes still require writes | false / false |

QA readiness endpoints appear after the new source is deployed. `/health/live` checks process liveness; `/health/ready` checks database/Dispatcher readiness and matching source revision. A 503 is not a reason to replay a connection.

For a QA test feed, choose a distinct `feedName`, suitable `gameIds`, future matching `eventLinkId`, and private webhook. Enable write/message flags and run the workflow. This creates real QA records and can post to Discord. To reconnect an existing subscription, get its current subscription version and run only Connect. The recovered QA subscription does not need another reconnect.

Feed and subscription versions differ. On 409, reload the corresponding resource before an intentional retry. Investigate failed/ambiguous delivery history first: reconnecting may send an acknowledgement and current digest.

## Endpoint corrections and optional examples

| Previous example | Current request |
| --- | --- |
| `GET QueryPlayerStandings` | `POST /api/players/GetPlayerStandings` with `playerId` |
| Singular bracket path with trailing slash | `GET /api/players/GetBracketPathByPlayerIds?playerIds=...`; repeat the key for multiple players |
| Discord connection under a feed ID | `POST /api/event-feeds/subscriptions/{subscriptionId}/discord/connect` |
| `InitializeDiscordServerListener` | `POST /api/exclusights/AddDiscordServerListener` |
| Duplicate/mislabeled bracket onboarding | Separate bracket runner and `/api/leagues/OnboardTournamentStandingstoLeague` |

Legacy endpoints retain their controller-specific authorization behavior; collection flags are client conveniences, not server authorization. Requests describe required fields. Numeric IDs and arrays must be valid JSON; missing/blank variables stop requests before sending.

An asynchronous bracket 202 response saves `operationId`; poll Bracket processing status instead of resubmitting. Discord OAuth must finish in its original authenticated session. Opening its URL in another browser does not transfer Postman's cookie. OAuth/socket integrations are manual examples and are not simulated locally.
