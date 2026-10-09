using Dapper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;

internal static class SchemaInspection
{
    public static async Task Run(string settingsPath, string? migrationPath = null)
    {
        var configured = await LoadConnectionString(settingsPath);
        var options = new NpgsqlConnectionStringBuilder(configured)
        {
            Timeout = 15,
            CommandTimeout = 30,
            ApplicationName = "Sengoku user schema inspection",
            IncludeErrorDetail = false
        };
        try
        {
            await using var conn = new NpgsqlConnection(options.ConnectionString);
            await conn.OpenAsync();
            if (migrationPath != null)
            {
                await ValidateMigration(conn, migrationPath);
                return;
            }
            await using var tx = await conn.BeginTransactionAsync();
            await conn.ExecuteAsync("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '20s';", transaction: tx);
            var queries = new (string Name, string Sql)[]
            {
                ("tables", """
                    SELECT n.nspname AS schema, c.relname AS name, c.relkind::text AS kind
                    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE c.relname IN ('users', 'players') AND n.nspname NOT LIKE 'pg_%'
                    """),
                ("columns", """
                    SELECT table_schema, table_name, column_name, data_type, udt_name, is_nullable, column_default
                    FROM information_schema.columns WHERE table_name IN ('users', 'players') ORDER BY table_schema, table_name, ordinal_position
                    """),
                ("constraints", """
                    SELECT conname, conrelid::regclass::text AS source_table, NULLIF(confrelid, 0)::regclass::text AS referenced_table,
                        contype::text AS type, condeferrable, condeferred, convalidated, pg_get_constraintdef(oid) AS definition
                    FROM pg_constraint WHERE conrelid IN ('public.users'::regclass, 'public.players'::regclass)
                        OR confrelid IN ('public.users'::regclass, 'public.players'::regclass) ORDER BY source_table, conname
                    """),
                ("indexes", """
                    SELECT tablename, indexname, indexdef FROM pg_indexes
                    WHERE schemaname = 'public' AND tablename IN ('users', 'players') ORDER BY tablename, indexname
                    """),
                ("triggers", """
                    SELECT tgrelid::regclass::text AS table_name, tgname, pg_get_triggerdef(oid) AS definition
                    FROM pg_trigger WHERE tgrelid IN ('public.users'::regclass, 'public.players'::regclass) AND NOT tgisinternal
                    """),
                ("link_summary", """
                    SELECT 'users.user_link' AS field, count(*) AS rows, count(*) FILTER (WHERE user_link > 0) AS linked,
                        (SELECT count(*) FROM (SELECT user_link FROM public.users WHERE user_link > 0 GROUP BY user_link HAVING count(*) > 1) d) AS duplicate_groups FROM public.users
                    UNION ALL SELECT 'users.player_id', count(*), count(*) FILTER (WHERE player_id > 0),
                        (SELECT count(*) FROM (SELECT player_id FROM public.users WHERE player_id > 0 GROUP BY player_id HAVING count(*) > 1) d) FROM public.users
                    UNION ALL SELECT 'players.user_link', count(*), count(*) FILTER (WHERE user_link > 0),
                        (SELECT count(*) FROM (SELECT user_link FROM public.players WHERE user_link > 0 GROUP BY user_link HAVING count(*) > 1) d) FROM public.players
                    UNION ALL SELECT 'players.startgg_link', count(*), count(*) FILTER (WHERE startgg_link > 0),
                        (SELECT count(*) FROM (SELECT startgg_link FROM public.players WHERE startgg_link > 0 GROUP BY startgg_link HAVING count(*) > 1) d) FROM public.players
                    """)
            };
            foreach (var (name, sql) in queries)
            {
                var rows = await conn.QueryAsync(sql, transaction: tx);
                Console.WriteLine(name + ": " + JsonConvert.SerializeObject(rows));
            }
            await tx.RollbackAsync();
        }
        catch (PostgresException ex)
        {
            Console.Error.WriteLine($"PostgreSQL {ex.SqlState}: {ex.MessageText}");
            Environment.ExitCode = 1;
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            Console.Error.WriteLine($"Database hostname could not be resolved ({ex.SocketErrorCode})." );
            Environment.ExitCode = 1;
        }
        catch (NpgsqlException ex)
        {
            Console.Error.WriteLine($"Database connection failed ({ex.GetType().Name}; inner: {ex.InnerException?.GetType().Name}).");
            Environment.ExitCode = 1;
        }
    }
    internal static async Task<string> LoadConnectionString(string settingsPath)
    {
        var settings = JObject.Parse(await File.ReadAllTextAsync(settingsPath));
        return (string?)settings["ConnectionStrings"]?["AlexandriaConnectionString"]
            ?? throw new InvalidOperationException("AlexandriaConnectionString is missing.");
    }

    private static async Task ValidateMigration(NpgsqlConnection conn, string migrationPath)
    {
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            // Copy only linking IDs, never names, emails or passwords. All DDL is
            // redirected to connection-local temporary tables and rolled back.
            await conn.ExecuteAsync("""
                SET LOCAL lock_timeout = '3s';
                SET LOCAL statement_timeout = '30s';
                CREATE TEMP TABLE users ON COMMIT DROP AS SELECT id, user_link, player_id FROM public.users;
                CREATE TEMP TABLE players ON COMMIT DROP AS SELECT id, startgg_link, user_link FROM public.players;
                ALTER TABLE pg_temp.players ADD CONSTRAINT unique_startgg_link UNIQUE (startgg_link);
                """, transaction: tx);
            var sql = await File.ReadAllTextAsync(migrationPath);
            sql = System.Text.RegularExpressions.Regex.Replace(sql, @"(?m)^\s*(BEGIN|COMMIT);\s*$", "");
            // This checker deliberately supports only the onboarding migration's
            // ADD COLUMN and partial-index statements, not arbitrary migration SQL.
            var statements = System.Text.RegularExpressions.Regex.Replace(sql, @"--[^\r\n]*", "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var statement in statements)
            {
                const string allowed = @"\A(?:ALTER\s+TABLE\s+public\.users\s+ADD\s+COLUMN\s+IF\s+NOT\s+EXISTS\s+startgg_profile\s+jsonb|CREATE\s+UNIQUE\s+INDEX\s+IF\s+NOT\s+EXISTS\s+\w+\s+ON\s+public\.(?:users|players)\s*\((?<column>user_link|player_id|startgg_link)\)\s+WHERE\s+\k<column>\s*>\s*0)\z";
                if (!System.Text.RegularExpressions.Regex.IsMatch(statement, allowed,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                    throw new InvalidOperationException("This checker supports only the user-profile column and linking indexes.");
            }
            sql = string.Join(";\n", statements.Select(statement =>
                System.Text.RegularExpressions.Regex.Replace(statement, @"public\.(users|players)",
                    match => "pg_temp." + match.Groups[1].Value.ToLowerInvariant(),
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant)));
            await conn.ExecuteAsync(sql, transaction: tx);
            await conn.ExecuteAsync(sql, transaction: tx);
            Console.WriteLine("PASS migration applied twice to temporary copies of current linking data; live tables unchanged.");
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }
}
