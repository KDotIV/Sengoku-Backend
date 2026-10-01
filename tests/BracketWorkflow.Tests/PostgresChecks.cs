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
            await connection.ExecuteAsync("""
                CREATE TABLE tournament_sets (id text PRIMARY KEY, playerone_id integer, playerone_name text,
                    playertwo_id integer, playertwo_name text, last_updated timestamptz);
                CREATE TABLE bracket_paths (id integer PRIMARY KEY, tournament_link integer, tournament_name text,
                    event_link integer, round_num text, player_id integer, last_updated timestamptz, set_ids text[]);
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
            Assert(await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM tournament_sets") == 1, "duplicate cards");
            Assert((await store.GetAsync(finished.OperationId))!.Result.Status == "Completed", "completed checkpoint committed");
            Console.WriteLine("PASS PostgreSQL atomic final save and concurrent idempotent path writes");
        }
        finally { await admin.ExecuteAsync($"DROP SCHEMA {schema} CASCADE"); }
    }
    static void Assert(bool condition, string description) { if (!condition) throw new Exception(description); }
}
