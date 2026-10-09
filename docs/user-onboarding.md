# Linking start.gg profiles

For the new registration, login, session and CSRF contract, see [account-authentication.md](account-authentication.md). Public profile association below is not verified ownership.

Apply `database/migrations/20261006_user_startgg_profile.sql` before using the workflow.
It adds `users.startgg_profile` (JSONB) and unique indexes on positive
`users.user_link` and `users.player_id`. It reuses the existing
`players.startgg_link` unique constraint. Imported players can share a
`user_link`; the migration preserves those rows rather than imposing a new
uniqueness rule on historical player data.

`IUserOperations` orchestrates start.gg queries and `IUserService` persists the result.
`UserService` does not depend on player services or workflows. The API registers both;
worker and scheduler registrations also use the independent UserService constructor.

CreateUser now returns the inserted local user ID (zero when no row was inserted). Call either method from an authorized onboarding flow after creating the local user/player:

```csharp
await operations.LinkStartggProfileBySlug(localUserId, localPlayerId,
    "https://start.gg/user/b1a179d8", cancellationToken);
// Or use the start.gg USER ID (not the slug or player ID):
await operations.LinkStartggProfileByUserId(localUserId, localPlayerId,
    876630, cancellationToken);
```

Full start.gg profile URLs, `user/b1a179d8`, and `b1a179d8` are accepted.
Queries use GraphQL variables and the published schema:
https://smashgg-schema.netlify.app/reference/query.doc.html

| Source | Destination |
| --- | --- |
| `user.id` | `users.user_link`, `players.user_link` |
| `user.player.id` | `players.startgg_link` |
| Existing local player ID | `users.player_id`, unchanged `players.id` |
| Returned `id`, `name`, `slug`, `player` | `users.startgg_profile` JSONB |

The existing local records must exist. The transaction locks both rows, preserves
local names/email/password and IDs, rejects conflicting associations, and refreshes
the snapshot when the same link is repeated. PostgreSQL failures are caught and
wrapped with their original exception. Concurrent serialization conflicts require
retrying the operation; partial updates roll back.

This is a public-profile association, not proof of account ownership. The calling
application must authorize access to the local user/player; use start.gg OAuth if
verified ownership is required. No new unauthenticated write endpoint is exposed.
The existing `SyncStartggDataToPlayer` HTTP endpoint remains a legacy preview.

Run checks:

```powershell
dotnet run --project tests/UserOnboarding.Tests/UserOnboarding.Tests.csproj
```

For database tests, set `USER_TEST_POSTGRES` to an isolated PostgreSQL database
connection string. The test creates a unique temporary schema, applies the migration
there, tests repeat linking/conflicts/rollback, and drops only that schema afterward.
The mocked player ID in lookup tests is synthetic; it is not asserted to be Oni_Shogi's
real player ID. No live start.gg credentials are needed for these tests.

## Database inspection and migration verification

Inspection of the configured database found 15 duplicate positive
`players.user_link` groups and an existing `unique_startgg_link` constraint.
The original migration failed with PostgreSQL SQLSTATE `23505` while creating
`players_startgg_user_unique`. The "Circular reference detected" client message
was not reproduced by a direct database connection. There is a foreign key from
`users.player_id` to `players.id`, but no reverse foreign key between those tables.

The revised migration adds only the missing JSONB column and user association
indexes. It does not merge or delete players. Linking still rejects ambiguous
existing player associations; reconcile those explicitly before linking them.
New users need a real local player ID because `users.player_id` is NOT NULL and
references `players.id` (the existing NOT VALID foreign key still checks new writes).

Read the schema without printing the configured connection string:

```powershell
dotnet run --project tests/UserOnboarding.Tests/UserOnboarding.Tests.csproj -- --inspect-database SengokuProvider.API/appsettings.json
```

Verify this migration twice against temporary copies of current linking IDs;
all DDL is redirected to temporary tables and rolled back:

```powershell
dotnet run --project tests/UserOnboarding.Tests/UserOnboarding.Tests.csproj -- --validate-migration SengokuProvider.API/appsettings.json database/migrations/20261006_user_startgg_profile.sql
```

Run the persistence tests using that connection in a generated isolated schema,
which is dropped afterward:

```powershell
dotnet run --project tests/UserOnboarding.Tests/UserOnboarding.Tests.csproj -- --test-postgres SengokuProvider.API/appsettings.json
```

The inspection and validation commands do not apply the migration to live tables.
