using Dapper;
using Npgsql;
using SengokuProvider.Library.Models.User;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Users;

static class PostgresChecks
{
    public static async Task Run(string? connectionString = null)
    {
        var configured = connectionString ?? Environment.GetEnvironmentVariable("USER_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Console.WriteLine("SKIP PostgreSQL integration checks: USER_TEST_POSTGRES is not set.");
            return;
        }
        var schema = "user_tests_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(configured);
        await admin.OpenAsync();
        await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
        try
        {
            var scoped = new NpgsqlConnectionStringBuilder(configured) { SearchPath = schema }.ConnectionString;
            await using var conn = new NpgsqlConnection(scoped);
            await conn.OpenAsync();
            await conn.ExecuteAsync("""
                CREATE TABLE users (id int PRIMARY KEY, user_name varchar NOT NULL, email varchar NOT NULL UNIQUE, password varchar NOT NULL, permission_checksum varchar UNIQUE, player_id int NOT NULL, user_link int NOT NULL);
                CREATE TABLE players (id int PRIMARY KEY, player_name varchar NOT NULL, startgg_link int CONSTRAINT unique_startgg_link UNIQUE, user_link int, last_updated timestamp);
                INSERT INTO users (id, user_name, email, password, player_id, user_link) VALUES (100, 'local', 'local@example.test', 'unchanged', 200, 0), (101, 'other', 'other@example.test', 'unchanged', 201, 0);
                INSERT INTO players VALUES (200, 'local tag', NULL, 0, now()), (201, 'other tag', NULL, 0, now()),
                    (202, 'imported A', 9001, 999, now()), (203, 'imported B', 9002, 999, now()),
                    (204, 'new local player', NULL, NULL, now());
                ALTER TABLE users ADD CONSTRAINT user_players_id_fk FOREIGN KEY (player_id) REFERENCES players(id) NOT VALID;
                """);
            // Commands documented for this harness run from the repository root.
            var migration = await File.ReadAllTextAsync("database/migrations/20261006_user_startgg_profile.sql");
            // Run the production migration against this isolated schema only.
            migration = migration.Replace("public.", schema + ".");
            await conn.ExecuteAsync(migration);
            await conn.ExecuteAsync(migration);
            Assert(await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM players WHERE user_link = 999") == 2, "migration preserves imported duplicate user links");
            Assert(await conn.ExecuteScalarAsync<int>("SELECT count(*) FROM pg_indexes WHERE schemaname = current_schema() AND tablename = 'players'") == 2, "migration reuses existing player indexes");
            var service = new UserService(scoped, new IntakeValidator());
            var createdId = await service.CreateUser("new_user", "new@example.test", "existing_contract", 204);
            Assert(createdId >= 100000 && await service.CheckUserById(createdId), "registration returns local identity" );
            var profile = new CommonUserNode { Id = 876630, Slug = "user/b1a179d8", Name = "Oni_Shogi", Player = new Player { Id = 456, GamerTag = "Oni_Shogi" } };
            await service.SaveStartggProfile(100, 200, profile);
            await service.SaveStartggProfile(100, 200, profile);
            Assert(await conn.ExecuteScalarAsync<bool>("SELECT player_id = 200 AND user_link = 876630 AND startgg_profile->>'slug' = 'user/b1a179d8' AND password = 'unchanged' FROM users WHERE id = 100"), "user snapshot and local identity");
            Assert(await conn.ExecuteScalarAsync<bool>("SELECT startgg_link = 456 AND user_link = 876630 AND player_name = 'local tag' FROM players WHERE id = 200"), "player association");
            Assert((await service.GetUserById(100))!.UserName == "local", "typed user read");
            Assert(await service.CheckUserById(100) && !await service.CheckUserById(999), "user existence");
            try { await service.SaveStartggProfile(101, 201, profile); throw new Exception("Expected conflict"); }
            catch (InvalidOperationException) { }
            Assert(await conn.ExecuteScalarAsync<int?>("SELECT startgg_link FROM players WHERE id = 201") == null, "conflict rollback");
            // Force the second UPDATE to fail and prove that the first UPDATE rolls back.
            await conn.ExecuteAsync("ALTER TABLE users ADD CONSTRAINT reject_test_profile CHECK (user_link <> 888)");
            profile.Id = 888; profile.Player.Id = 777;
            try { await service.SaveStartggProfile(101, 201, profile); throw new Exception("Expected database failure"); }
            catch (ApplicationException) { }
            Assert(await conn.ExecuteScalarAsync<int?>("SELECT startgg_link FROM players WHERE id = 201") == null, "atomic rollback after player update");
            Console.WriteLine("PASS PostgreSQL repeatable migration with legacy duplicates and existing constraints, linking, idempotency, identity preservation, conflicts and transaction rollback");
        }
        finally { await admin.ExecuteAsync($"DROP SCHEMA {schema} CASCADE"); }
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
