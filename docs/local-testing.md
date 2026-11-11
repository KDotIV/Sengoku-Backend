# QA and local testing

QA delivery belongs to the Azure `sengoku-dispatcher` App Service. A locally started Dispatcher refuses the QA database, even when delivery flags are disabled: webhook connections also encrypt values and must go through Azure. Provider retains session authentication, ownership checks and CSRF.

## Start an isolated local environment

Prerequisites: .NET 8 SDK, PowerShell 7.4 or newer, and either PostgreSQL 18 server tools or Docker. Restore the private fixtures first for a new checkout (below).

From the repository root:

```powershell
pwsh ./scripts/Start-LocalEnvironment.ps1
# Explicit alternatives:
pwsh ./scripts/Start-LocalEnvironment.ps1 -DatabaseRuntime Docker
pwsh ./scripts/Start-LocalEnvironment.ps1 -DatabaseRuntime Native -PostgresBin 'C:/Program Files/PostgreSQL/18/bin'
```

Auto prefers installed PostgreSQL server tools; otherwise it uses a temporary `postgres:18` container. Each launch creates a new database with a random password and loopback-only random port. It does not use your installed PostgreSQL service or QA. Ctrl+C stops both API hosts and the database/container started by the launcher. Ignored files under `.local-e2e/run-*` remain for diagnosis; Docker data disappears with the container.

| Local service | Address / credentials |
| --- | --- |
| Provider | `https://localhost:7028` |
| Dispatcher | `http://127.0.0.1:7081` (requires a test-only internal key) |
| Synthetic administrator | `admin@local.test` / `LocalOnly-12345!` |
| Captured digest payloads | `GET https://localhost:7028/_local/messages` |

The launcher generates a temporary self-signed HTTPS certificate without changing your trust store. In Postman, disable certificate verification **for local requests only**; keep it enabled for QA. Session and antiforgery cookies remain Secure and HttpOnly. See [the Postman guide](../postman/README.md).

The test executable constructs configuration without inheriting developer appsettings, environment credentials or user secrets. Provider and Dispatcher have separate persisted local key directories. Neither uses QA keys.

## Coverage and limits

The harness starts the actual Provider and Dispatcher bootstraps. PostgreSQL, SQL functions/triggers, authentication, CSRF, feed creation/connection, tournament intake, outbox publishing, worker handlers, serialization and delivery persistence run normally.

Only external transports are replaced: start.gg supplies a synthetic future tournament, Discord captures acknowledgements/digests, and Service Bus uses an in-process SDK double. Unexpected external HTTP destinations are rejected. This does not test AMQP, broker locks, Azure permissions, live OAuth or real Discord transport; QA readiness probes the actual broker. The Docker option containers PostgreSQL; the .NET hosts run locally.

The fixture provides tournament `900001`, bracket `900002` and game `1`. The Local Postman environment supplies these IDs. Optional league/bracket examples need additional fixture data. The local upstream stub is tailored to tournament intake, not a complete start.gg emulator.

## Run verification and exit

```powershell
pwsh ./tests/Run-LocalTests.ps1
pwsh ./tests/Run-LocalTests.ps1 -DatabaseRuntime Docker -Configuration Release
```

This builds the solution, runs six regression harnesses with isolated database schemas, then runs the HTTP workflow. Checks cover anonymous rejection, missing CSRF, Secure cookies, login/session/logout, outbox/broker/worker delivery, readiness rejection for missing keys/stale workers/broker failure, the QA guard, and Dispatcher restart without duplicate sends. Owned database servers stop on normal completion or handled failure.

For an already running **disposable** local database, supply `-DatabaseSettings <private.json>` containing `ConnectionStrings:AlexandriaConnectionString`. Host must be loopback and database name must begin with `sengoku_local`. This adds synthetic seed records; use a dedicated test database. The runner does not stop a server it did not start. `-NoBuild` uses the previously built configuration.

## Private database fixtures

Definitions, SQL migrations and schema snapshots are excluded from public source. Public clones can build and run non-database tests; integration tests require the owner's private fixture bundle. Application code still contains data-access queries/models. Ignoring files does not conceal those contracts or remove historical commits.

An authorized developer imports the privately shared ZIP:

```powershell
pwsh ./scripts/Import-PrivateDatabaseFixtures.ps1 -ZipPath 'C:/private/private-database-fixtures.zip'
```

The owner regenerates the ZIP and compressed CI value from the ignored `database/` directory:

```powershell
pwsh ./scripts/Export-PrivateDatabaseFixtures.ps1
# Outputs: .local-e2e/private-fixtures/ (ignored).
```

The export is a manifest of required fixtures, not a database data dump. `local-baseline.sql` is a schema-only QA snapshot; `local-seed.sql` contains synthetic data. `event-feed-calendar-current.sql` reconstructs the currently installed calendar contract for tests. It is **not** a production migration; do not apply it to QA as a recovery action.

The trusted `master` integration job imports repository Actions secret `PRIVATE_DATABASE_FIXTURES_B64`, then uses a temporary PostgreSQL service. This secret was configured November 11, 2026. Update it from `.local-e2e/private-fixtures/private-database-fixtures.base64.txt` when fixtures change. Do not publish bundles as Actions artifacts. There is no pull-request trigger; fork/branch test jobs receive no private definitions. Trusted `master` workflow/test code can access the secret and needs corresponding review.

Public/non-database verification:

```powershell
dotnet build SengokuProvider.API/SengokuProvider.API.sln -m:1
pwsh ./tests/Run-RegressionTests.ps1
```

## QA recovery verified November 11, 2026

The November 4 local Dispatcher protected the destination with a key absent from Azure. Azure recorded `Failed / SecretUnavailable`, and the completion function disabled the subscription. Reconnecting through Azure encrypted the destination with its persistent ring. A new delivery was confirmed `Sent` at 22:21:09 UTC (5:21:09 PM Eastern) on November 11. No schema changes or bulk replay were needed.

See [deployment checks](../.github/DEPLOYMENT.md) for key persistence and readiness. Recovery is live; new code/workflow checks take effect after deployment.
