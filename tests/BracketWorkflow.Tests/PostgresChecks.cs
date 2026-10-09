using Dapper;
using Npgsql;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Players;
using SengokuProvider.Worker.Handlers;

static class PostgresChecks
{
    public static async Task Run()
    {
        var configured = Environment.GetEnvironmentVariable("BRACKET_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured))
        {
            Console.WriteLine("SKIP PostgreSQL integration checks: BRACKET_TEST_POSTGRES is not set.");
            return;
        }
        // The name is generated locally, never supplied by configuration or a test.
        var schema = "bracket_tests_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(configured);
        await admin.OpenAsync();
        await admin.ExecuteAsync($"CREATE SCHEMA {schema}");
        try
        {
            var scoped = new NpgsqlConnectionStringBuilder(configured) { SearchPath = schema }.ConnectionString;
            await using var connection = new NpgsqlConnection(scoped);
            await connection.OpenAsync();
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "database/migrations/001_bracket_processing.sql"))) root = root.Parent;
            if (root == null) throw new InvalidOperationException("Migration file not found.");
            var migration = await File.ReadAllTextAsync(Path.Combine(root.FullName, "database/migrations/001_bracket_processing.sql"));
            await connection.ExecuteAsync(migration);
            await connection.ExecuteAsync(migration); // safe to apply twice
            var matchupMigration = await File.ReadAllTextAsync(Path.Combine(root.FullName, "database/migrations/002_bracket_matchup_keys.sql"));
            await connection.ExecuteAsync(matchupMigration);
            await connection.ExecuteAsync(matchupMigration);
            await connection.ExecuteAsync("""
                CREATE TABLE tournament_sets (id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY, playerone_id integer, playerone_name text,
                    playertwo_id integer, playertwo_name text, last_updated timestamptz, entrantone_id integer, entranttwo_id integer, tournament_link integer, path_step integer, path_set_id text);
                CREATE TABLE bracket_paths (id integer PRIMARY KEY, tournament_link integer, tournament_name text,
                    event_link integer, round_num text, player_id integer, last_updated timestamptz, set_ids integer[], bracket_id integer, entrant_id integer, player_startgg_link integer);
                INSERT INTO tournament_sets (id, playerone_name) OVERRIDING SYSTEM VALUE VALUES (1, 'Legacy set');
                """);
            var store = new BracketCheckpointStore(scoped);
            try
            {
                await store.ExecuteAsync("rollback", async (_, conn, tx) =>
                {
                    await store.EnqueueAsync(conn, tx, Guid.NewGuid(), "players", new PlayerReceivedData
                    { Command = new ResumeBracketProcessingCommand { OperationId = Guid.NewGuid() }, MessagePriority = MessagePriority.SystemIntake }, DateTime.UtcNow);
                    throw new InvalidOperationException("Simulated failure before checkpoint commit");
                });
            }
            catch (InvalidOperationException) { }
            Assert(await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM bracket_processing_outbox") == 0, "outbox rollback");
            Assert(await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM bracket_processing_checkpoints") == 0, "checkpoint rollback");
            Console.WriteLine("PASS PostgreSQL checkpoint/outbox rollback and repeatable migration");

            var concurrent = Enumerable.Range(0, 12).Select(_ => store.ExecuteAsync("serialized", (current, _, _) =>
            {
                var checkpoint = current ?? Fixture.Checkpoint(); checkpoint.RequestKey = "serialized"; checkpoint.Attempts++;
                return Task.FromResult(checkpoint);
            }));
            await Task.WhenAll(concurrent);
            var payload = await connection.QuerySingleAsync<string>("SELECT payload::text FROM bracket_processing_checkpoints WHERE request_key = 'serialized'");
            var serialized = Newtonsoft.Json.JsonConvert.DeserializeObject<BracketProcessingCheckpoint>(payload)!;
            Assert(serialized.Attempts == 12, "concurrent resumes did not lose progress");
            Console.WriteLine("PASS PostgreSQL concurrent checkpoint serialization");

            var intake = new PlayerIntakeService(scoped, null!, null!, null!, null!);
            var finished = Fixture.Checkpoint(); finished.RequestKey = "complete";
            finished.ExpectedOpponents = [new(200, 20, "Opponent", "A", "Direct") { PathSetId = "1" }];
            BracketCardBuilder.Build(finished, [Fixture.Legend(20, 2)]);
            await store.ExecuteAsync("complete", async (_, conn, tx) =>
            {
                finished.Result = await intake.SaveVictoryPathData(finished.Data, conn, tx);
                return finished;
            });
            await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => intake.SaveVictoryPathData(finished.Data)));
            Assert(await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM bracket_paths") == 1, "duplicate paths");
            Assert(await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM tournament_sets") == 2, "duplicate cards or legacy set overwritten");
            Assert(await connection.ExecuteScalarAsync<string>("SELECT playerone_name FROM tournament_sets WHERE id = 1") == "Legacy set", "legacy ID collision avoided");
            Assert(await connection.ExecuteScalarAsync<string>("SELECT pg_typeof(set_ids)::text FROM bracket_paths LIMIT 1") == "integer[]", "integer array contract");
            Assert((await store.GetAsync(finished.OperationId))!.Result.Status == "Completed", "completed checkpoint committed");
            Console.WriteLine("PASS PostgreSQL atomic final save and concurrent idempotent path writes");

            var nomar = Fixture.StoredCheckpoint();
            BracketCardBuilder.Build(nomar, nomar.ExpectedOpponents.Select((opponent, index) => Fixture.Legend(opponent.PlayerLink, 800001 + index)));
            var mappingsBefore = await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM bracket_matchup_keys");
            try
            {
                await store.ExecuteAsync(nomar.RequestKey, async (_, conn, tx) =>
                {
                    await intake.SaveVictoryPathData(nomar.Data, conn, tx);
                    throw new InvalidOperationException("Simulated failure after writing sets and path");
                });
                throw new Exception("Expected transaction failure");
            }
            catch (InvalidOperationException) { }
            Assert(await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM bracket_matchup_keys") == mappingsBefore, "mapping allocation rolled back with save");
            Assert(await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM bracket_paths") == 1, "path rolled back with checkpoint");
            await store.ExecuteAsync(nomar.RequestKey, async (_, conn, tx) =>
            {
                nomar.Result = await intake.SaveVictoryPathData(nomar.Data, conn, tx);
                return nomar;
            });
            var firstIds = await connection.QuerySingleAsync<int[]>("SELECT set_ids FROM bracket_paths WHERE player_id = 774869");
            Assert(firstIds.Length == 4 && firstIds.Distinct().Count() == 4, "four distinct integer set references");
            var replay = await intake.SaveVictoryPathData(nomar.Data);
            Assert(replay.Successful.Select(int.Parse).Order().SequenceEqual(firstIds.Order()), "resume reuses stable integer identities");
            Assert(await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM bracket_paths WHERE player_id = 774869") == 1, "NOMAR replay has one path");
            Assert(await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM tournament_sets WHERE id = ANY(@ids)", new { ids = firstIds }) == 4, "all numeric references resolve");
            Console.WriteLine("PASS PostgreSQL NOMAR payload, integer[] persistence, mapping rollback and replay");
            var generatedId = await connection.ExecuteScalarAsync<int>(
                "INSERT INTO tournament_sets (playerone_name) VALUES ('Independent identity insert') RETURNING id");
            Assert(!firstIds.Contains(generatedId), "existing identity sequence remains usable by other callers");

            // Exercise deployments where IDs are application-assigned rather than identity/serial.
            await connection.ExecuteAsync("ALTER TABLE tournament_sets ALTER COLUMN id DROP IDENTITY");
            nomar.ExpectedOpponents.Add(new ExpectedOpponent(999, 40, "Another candidate", "D", "Direct") { PathSetId = "103228116" });
            BracketCardBuilder.Build(nomar, [Fixture.Legend(40, 800005)]);
            var expanded = await intake.SaveVictoryPathData(nomar.Data);
            var expandedIds = expanded.Successful.Select(int.Parse).ToArray();
            Assert(expandedIds.Length == 5 && expandedIds.Distinct().Count() == 5, "same-round candidates have distinct database IDs");
            Assert(firstIds.All(expandedIds.Contains), "fallback allocation preserves prior mappings");
            Assert(!expandedIds.Contains(generatedId) && !expandedIds.Contains(1), "fallback skips existing IDs");
            Console.WriteLine("PASS PostgreSQL fallback allocation and multiple candidates in one round");
        }
        finally { await admin.ExecuteAsync($"DROP SCHEMA {schema} CASCADE"); }
    }
    static void Assert(bool condition, string description) { if (!condition) throw new Exception(description); }
}
