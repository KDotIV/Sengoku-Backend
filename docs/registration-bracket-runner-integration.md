# Registration and bracket runner: backend completion and frontend handoff

Updated 2026-10-09. This supersedes the backend-gap observations in the frontend's original `registration-bracket-runner-integration.md`. All URLs below include `/api`. Frontend application code was not changed.

## Comparison with the original gap report

| Requirement | Before | Implemented backend behavior |
| --- | --- | --- |
| Registration identity | Text message; no usable local identity | JSON `userId`, `playerId`, `response`; atomic local player/account creation; passwords hashed |
| Login and current account | No authentication | Backend-owned, revocable eight-hour sessions; Login, Session, Logout and CSRF endpoints |
| Start.gg ownership | Public preview and internal slug-based persistence only | Session-bound OAuth authorization-code flow; links only `currentUser`; verified timestamp and provider subject |
| Existing imported player | New account's placeholder could conflict with imported history | Verified identity adopts the exact unowned imported player; preserves history; conflicting ownership returns 409 |
| Preview IDs | External ID sometimes occupied local `playerId` | `playerId` is local or 0; nullable `localPlayerId`; separate `startggPlayerId`; preview remains unverified |
| Multiple tournaments/pools | Rows collapsed by player | Groups by local player + stable `bracketPathId`; both singular/plural ID routes return arrays of all paths |
| Standings and entrants | Repeated or synthesized data | Actual latest stored standing and entrant identity; empty results when no standing exists |
| Empty path | Completed without saving anything | Persists and returns the path with an empty `entrantSetCards` array |
| Overview response | Computed cards discarded | `GetTournamentCardsByPlayerIDs` returns its result body |
| Schedule/order | Missing metadata | Nullable `startTime`, `endTime`, `lifecycle`; ordered candidate `pathStep`/`pathSetId`; `matchStatus: Candidate` |
| Tournament bootstrap | Event had to exist already | API verifies phase/bracket/event ownership against Start.gg and imports missing tournament metadata |
| Requesting player's Legend | Only opponents considered | Checkpoint waits for the requesting player's Legend too; worker creates Legends even with no historical standings |
| Read Legend | No focused player read | Public `GET /api/players/GetLegendByPlayerId?playerId=...` returns an array; `placements` are placement values, not standing IDs |
| Terminal operation retry | Same request stayed failed/expired | Owner-only, CSRF-protected `POST /api/players/BracketProcessing/{operationId}/Retry` reopens the saved snapshot |
| Public lookup | Local IDs required | Anonymous player-name and game-event-slug searches described below |

Existing background checkpoint/outbox processing and frontend polling remain in use. Import still requires the player to be an entrant in a singles bracket. Team entrants and incomplete feeder-set graphs are rejected explicitly.

## Frontend implementation still needed

1. Add real login/logout/account restoration. All account requests use `withCredentials: true` / `credentials: 'include'`. Fetch `GET /api/user/Csrf`, retain `requestToken` in memory, and include `X-CSRF-TOKEN` on protected POSTs. Fetch a new CSRF token after login/logout. Never store passwords or session cookies in browser storage.
2. Change registration from text to JSON. `POST /api/user/CreateUser` accepts `userName`, `email`, `password` (existing topic 401 may remain). Password length is 12–1024. Response contains local IDs, but registration does not log in. Call `POST /api/user/Login` with email/password, then restore account state using `GET /api/user/Session`.
3. Add a Verify Start.gg action. `GET /api/user/Startgg/Link` returns `{ enabled, link }`; null link means unverified. `POST /api/user/Startgg/Authorize` with CSRF returns `{ authorizationUrl }`. Open that URL in a popup or navigate to it. The user consents on Start.gg; the backend callback validates the initiating session and single-use state, exchanges the code and verifies `currentUser`.
4. The callback currently displays JSON at the API callback URL; there is no frontend redirect or `postMessage` bridge. For a popup flow, poll the authenticated Link endpoint while waiting and stop on success/timeout/closed popup. A full-page flow must provide a way back to the frontend and recheck Link. Do not trust query-string IDs or a public profile preview as proof. Refresh Session after success: `playerId` can change when existing imported data is adopted. Clear caches/pending selections belonging to the old placeholder. The API never sends provider tokens to the browser.
5. Replace the manual player ID input for imports with the current session's local `playerId`. Only verified accounts can import. Public browsing can retain a separately selected player without changing account identity. Preview success must use the external `startggPlayerId`, not require a positive local ID.
6. Import with `POST /api/players/OnboardBracketPathByBracketSlug`, body `{ bracketSlug, playerId }`, credentials and CSRF. Use the existing five-second polling for 202/Pending via `GET /api/players/BracketProcessing/{operationId}`. A 200 poll may still be Pending. Load paths and Legend only when Completed. Polling requires the owning session; 401 means sign-in expired, 403 on import means wrong player or missing verification, 404 on polling/retry means unavailable to this account. Respect 429 on account endpoints.
7. For Failed/Expired, offer an explicit Retry action using the Retry endpoint with CSRF. It keeps the operation ID and retries unresolved dependencies in the saved snapshot; it does not refetch a live bracket. Reposting the original import is not a reset. Completed operations are unchanged. Stop polling on logout/account change and never replay an import automatically after ambiguous network failures.
8. Render each result independently using `bracketPathId` as the stable key, with `playerTournamentCard.playerID`, event and bracket labels. One player can have multiple results. Both ID routes now return arrays (the singular route is a breaking response change). Do not deduplicate by player ID or tournament ID, because separate pools would disappear.
9. Add public name/event searches using the routes below. Show no stored results as an empty state. These reads do not fetch/import new Start.gg brackets. Different players can share a name; show their local IDs/event context and preserve all matches.
10. Sort candidates by `pathStep` when available; candidates at the same step are alternatives, not sequential confirmed opponents. Preserve the server ordering for legacy null steps. Continue to label all cards as candidates. Use actual `startTime`/`endTime`; either may be null. `lifecycle` is Upcoming, InProgress, Completed or Unknown. Do not infer an upcoming event merely because its status is Unknown. Schedule/state is a stored snapshot, not a live tournament feed.

Preview remains `POST /api/user/SyncStartggDataToPlayer`. Its `verified` field is false; account verification is read only from the authenticated Link endpoint. Existing public preview associations are never automatically promoted to verified ownership.

## Public search contracts

All return `BracketVictoryPathData[]`, including `[]` when nothing stored matches. Invalid input returns 400. Query parameters must be URL encoded.

- `GET /api/players/GetBracketPathByPlayerName?playerName=...`: trimmed, exact, case-insensitive gamer tag; returns all matching local players and paths. It is not a substring search.
- `GET /api/players/GetBracketPathByTournamentSlug?tournamentSlug=...`: accepts `https://start.gg/tournament/evo-france-2026/event/street-fighter-6-ps5`, the same URL without scheme, canonical `tournament/evo-france-2026/event/street-fighter-6-ps5`, or `street-fighter-6-ps5`.
- Full/canonical URLs identify the game event under that parent tournament. The bare suffix matches that game-event slug across all parent tournaments, preserving separate results. A parent-only `tournament/evo-france-2026` is rejected. The backend's tournament link is the game event, not the parent event.
- `GET /api/players/GetBracketPathByPlayerIds?playerIds=123&playerIds=456`: still public; preserves each player's distinct paths.

Each path includes `bracketPathId`, nullable `bracketId`, `playerStartggLink`, `tournamentLinkID`, `eventLinkID`, `tournamentSlug`, `tournamentName`, `roundNum`, nullable `startTime`/`endTime`, `lifecycle`, `playerTournamentCard`, and `entrantSetCards`. Candidate cards add nullable `pathStep`, `pathSetId`, and `matchStatus`. Existing rows may lack newly introduced metadata; the migration does not invent it.

## Deployment and configuration

Keep migration files in source control after applying them. Apply to each deployment in order (base application tables must already exist):

1. `database/migrations/001_bracket_processing.sql`
2. `database/migrations/002_bracket_matchup_keys.sql`
3. `database/migrations/20261006_user_startgg_profile.sql`
4. `database/migrations/20261009_user_authentication.sql`
5. `database/migrations/20261010_registration_bracket_e2e.sql`

The scripts are repeatable. Resolve legacy duplicate case-insensitive emails or account identity claims explicitly if uniqueness checks fail; do not merge owners automatically. The existing `get_bracket_victory_path` function is no longer used by these reads because its inner joins discard paths without opponent rows. QA schema inspection confirmed full event URL slugs, integer set references and nullable imported identity fields. Live application records were only read by this work; integration tests create/drop generated schemas. The repository owner reports applying migrations separately.

Configure `Authentication:AllowedOrigins` with exact HTTPS frontend origins, `Authentication:CookieSameSite` (Lax for same-site; None when genuinely cross-site), and a persistent protected `Authentication:DataProtectionKeyPath`. See [account-authentication.md](account-authentication.md) for session and CSRF details.

Register a Start.gg OAuth application and provide server-side configuration:

```json
{
  "StartggOAuth": {
    "ClientId": "<registered client ID>",
    "ClientSecret": "<secret from deployment secret store>",
    "RedirectUri": "https://your-api.example/api/user/Startgg/Callback"
  }
}
```

Redirect URI must exactly match the registered HTTPS callback. Missing configuration disables authorization initiation with 503. Only `user.identity` is requested; no email scope is needed. Provider tokens are used transiently for verification and are not stored, so no refresh-token job is needed. OAuth state expires after ten minutes and is tied to the initiating session. Logout/rotation invalidates its session. Expired state can be cleaned with `DELETE FROM startgg_oauth_states WHERE expires_at <= CURRENT_TIMESTAMP`; expired sessions likewise.

Deploy API, player/Legend queue consumers and bracket outbox worker together against the same database. Configure their Start.gg API token and Service Bus queues. Apply migrations before the updated code. Event start time/state are refreshed when importing; Start.gg's game event schema supplies no event end time, so `endTime` stays null unless genuinely supplied locally. Historical rows remain Unknown until metadata is available.

Existing plaintext passwords cannot log in. A verified password-reset/migration process, email verification and a complete review of unrelated legacy write endpoints remain separate deployment work. These are not represented as completed features here.

## Verification and remaining live acceptance

Regression suites cover hashing/session/CSRF/CORS, account ownership, OAuth state replay/expiry/session binding/denial/provider failure, imported-player adoption and conflict rollback, first-time Legend creation, multi-event/pool reads, empty paths, real standings, ordered candidates, public search semantics, retry checkpoints, outbox rollback, concurrent idempotent saves and player-query retry exhaustion. PostgreSQL checks use isolated generated schemas; provider responses are mocked.

Before release, test the registered OAuth application's real consent/callback/state round trip over HTTPS, cross-origin cookie behavior in target browsers, and a real verified-account import through deployed Service Bus workers. Neither a live OAuth consent nor a deployed queue/browser E2E run was performed by this change.

Official references: [Start.gg OAuth](https://developer.start.gg/docs/oauth/oauth-overview/), [OAuth scopes](https://developer.start.gg/docs/oauth/scopes/), [event schema](https://smashgg-schema.netlify.app/reference/event.doc.html).
