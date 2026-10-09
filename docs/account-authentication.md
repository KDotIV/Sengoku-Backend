# Backend account authentication and frontend handoff

Implemented in the backend. See [registration and bracket runner handoff](registration-bracket-runner-integration.md) for the current OAuth and bracket contracts and full migration order. Integration tests use isolated schemas; the repository owner applies deployment migrations separately.

## Setup before deployment

1. Apply `database/migrations/20261009_user_authentication.sql`. It adds an email uniqueness index (case-insensitive), widens password storage, and adds `user_sessions`. Resolve any duplicate case-insensitive emails explicitly; the migration must fail instead of merging accounts. Existing passwords are not changed by the migration.
2. Configure these values through environment variables or deployment configuration (do not commit secrets):

```json
{
  "Authentication": {
    "AllowedOrigins": ["https://your-frontend.example"],
    "CookieSameSite": "Lax",
    "DataProtectionKeyPath": "<persistent, access-restricted key directory>"
  }
}
```

`Authentication__AllowedOrigins__0` is the corresponding environment variable. Origins must be exact HTTPS origins without trailing slashes. Empty configuration permits same-origin access only. This replaces wildcard CORS for the API. Use HTTPS locally as well. If frontend and API are on different *sites*, set `CookieSameSite` to `None`; cookies remain Secure. Third-party cookie restrictions can still block that topology; same-site hosting is preferred. Persist and protect the ASP.NET Data Protection key ring and share it between instances to preserve antiforgery tokens. Configure trusted reverse-proxy forwarded headers at deployment if TLS terminates upstream; never trust arbitrary client-supplied forwarding headers.

3. Existing plaintext or differently hashed passwords deliberately cannot log in. Before rollout, implement a verified password-reset flow or perform a controlled migration for accounts whose existing format is known. Do not enable plaintext login as a fallback. Password reset/change must revoke all rows in `user_sessions` for that user. No reset endpoint is implemented in this change.
4. Schedule cleanup: `DELETE FROM user_sessions WHERE expires_at <= CURRENT_TIMESTAMP`. Expired sessions are already rejected even before cleanup. Session cookies end with the browser session and have a fixed server lifetime of eight hours; there is no sliding renewal. Login rotates the presented session token. Logout deletes it server-side, so copied cookies cannot revive a logged-out session.
5. Account entry is limited to 10 requests per remote IP per minute per API instance. Configure a trusted client IP behind proxies and shared edge rate limits for multi-instance deployments.

## Frontend request sequence

Use `credentials: 'include'` (Angular: `withCredentials: true`) for every account request, including obtaining the CSRF token. Do not store passwords or session tokens in browser storage. The session cookie is HTTP-only; returned numeric IDs are not credentials.

1. `GET /api/user/Csrf` returns `{ "requestToken": "..." }` and sets an HTTP-only antiforgery cookie. Retain the request token in memory and send it in `X-CSRF-TOKEN` on account POSTs.
2. `POST /api/user/CreateUser` with `{ "userName": "...", "email": "...", "password": "..." }` and the CSRF header. Passwords must have 12–1024 characters and can include spaces, punctuation and Unicode. The server atomically creates the local player and account, stores a salted PBKDF2-SHA256 hash, and returns `{ "userId": 123, "playerId": 456, "response": "..." }`. The IDs are local database IDs, not Start.gg IDs. Registration does **not** establish a session. Existing clients must change from reading a text message to JSON. Validation returns 400; unavailable account details return 409.
3. `POST /api/user/Login` with `{ "email": "...", "password": "..." }` and the CSRF header. Success sets the session cookie and returns `{ "userId": 123, "playerId": 456, "userName": "..." }`. Invalid credentials return 401 without disclosing which credential failed.
4. Fetch `GET /api/user/Csrf` again after login because antiforgery tokens are bound to the current identity.
5. `GET /api/user/Session` restores the local account IDs after navigation/reload. 401 means signed out or expired. No passwords, tokens, or full database account objects are returned.
6. `POST /api/user/Logout` with the current CSRF header revokes the current session and clears the cookie (204). Clear frontend account state, then obtain a new anonymous CSRF token before another login.

All session/CSRF/account responses disable caching. Missing/invalid CSRF tokens return 400. Authentication failures on protected endpoints return 401 rather than an HTML redirect. Throttled account entry returns 429. Do not automatically retry registration POSTs after ambiguous network failures.

## Bracket workflow

`POST /api/players/OnboardBracketPathByBracketSlug` requires a session and CSRF header. Body: `{ "bracketSlug": "https://start.gg/tournament/.../event/.../brackets/123/456", "playerId": 456 }`. `playerId` must equal the session's local player and that account must have completed Start.gg OAuth verification; another player's ID returns 403. The API does not accept a posted account ID as proof of ownership.

202 means `status: "Pending"`. Retain `operationId`, then poll `GET /api/players/BracketProcessing/{operationId}` with credentials. This endpoint returns 404 for unknown operations or operations belonging to another player. A 200 poll can still be Pending. Continue until Completed, Failed or Expired; stop polling on navigation/cancellation and do not automatically repeat the onboarding POST. The background worker must be deployed and running to advance pending work.

Only after Completed, request `GET /api/players/GetBracketPathByPlayerIds?playerIds=456` for opponent cards. Both singular and plural ID routes return arrays of distinct paths. Existing card/standings reads remain public; this change is not a blanket authorization audit of every legacy endpoint. Public bracket statistics and account ownership are separate concerns.

## Start.gg ownership verification

OAuth support is implemented. See the [current frontend handoff](registration-bracket-runner-integration.md) for configuration and the exact UI sequence.

- `GET /api/user/Startgg/Link`: authenticated verification status and local/provider IDs.
- `POST /api/user/Startgg/Authorize`: authenticated, CSRF-protected initiation returning an authorization URL.
- `GET /api/user/Startgg/Callback`: authenticated callback returning the verified association as JSON. It validates a ten-minute, single-use state tied to the initiating session, exchanges the code server-side and queries `currentUser` with `user.identity`.
- Only provider-returned identity can be linked. An existing unowned imported player is adopted atomically; conflicting account claims return 409. Refresh Session afterward because the local player ID may change.
- Provider tokens are neither persisted nor returned to the browser. Public profile lookup is still an unverified preview. Registration does not accept an arbitrary external account claim.

Configure `StartggOAuth:ClientId`, `ClientSecret`, and the exact HTTPS `RedirectUri` through deployment configuration. Apply the OAuth/path migration after the session migration. Real consent/callback and browser cookie behavior still require testing with the registered application.

Official references: [OAuth flow](https://developer.start.gg/docs/oauth/oauth-overview/) and [identity scope](https://developer.start.gg/docs/oauth/scopes/). OAuth proves control of a Start.gg account, not a person's legal identity.

Password-reset delivery, email verification and a complete authorization review of unrelated legacy endpoints remain separate work.

## Verification

```powershell
dotnet run --project tests/AccountAuthentication.Tests
# Optional DB + HTTP integration; creates/drops only a generated auth_tests_* schema:
dotnet run --project tests/AccountAuthentication.Tests -- SengokuProvider.API/appsettings.json
```

The integration harness never starts the production app's Discord/services. It uses an isolated test app and generated schema; its loopback HTTP transport simulates an HTTPS request context solely for secure-cookie tests. Browser/proxy/cross-site deployment behavior must also be checked against the configured HTTPS deployment.

Framework references:
https://learn.microsoft.com/aspnet/core/security/anti-request-forgery
https://learn.microsoft.com/aspnet/core/security/cors
