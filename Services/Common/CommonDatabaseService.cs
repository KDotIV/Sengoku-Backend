using Dapper;
using Npgsql;
using SengokuProvider.API.Models.Common;

namespace SengokuProvider.API.Services.Common
{
    public class CommonDatabaseService : ICommonDatabaseService
    {
        private readonly string _connectionString;

        public CommonDatabaseService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<int> CreateTable(string tableName, Tuple<string, string>[] columnDefinitions)
        {
            if (!IsValidIdentifier(tableName) || columnDefinitions.Any(cn => !IsValidIdentifier(cn.Item1)) || columnDefinitions == null)
            {
                throw new ArgumentException("Invalid table or column name.");
            }

            var columnsDefinition = string.Join(", ", columnDefinitions.Select(cn => $"\"{cn.Item1}\" {cn.Item2}"));
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                var createTableCommand = @$"CREATE TABLE IF NOT EXISTS ""{tableName}"" (
                    ""id"" SERIAL PRIMARY KEY,
                    {columnsDefinition}
                    );";

                return await conn.ExecuteAsync(createTableCommand);
            }
        }
        public async Task<int> CreateAssociativeTable()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();
                var createAssociativeTables = @"
                CREATE TABLE IF NOT EXISTS players_events (
                    player_id INTEGER REFERENCES players(id),
                    event_id INTEGER REFERENCES events(id),
                    PRIMARY KEY (player_id, event_id)
                );

                CREATE TABLE IF NOT EXISTS players_legends (
                    player_id INTEGER REFERENCES players(id),
                    legend_id INTEGER REFERENCES legends(id),
                    PRIMARY KEY (player_id, legend_id)
                );

                CREATE TABLE IF NOT EXISTS events_legends (
                    event_id INTEGER REFERENCES events(id),
                    legend_id INTEGER REFERENCES legends(id),
                    PRIMARY KEY (event_id, legend_id)
                );";

                return await conn.ExecuteAsync(createAssociativeTables);
            }
        }
        public Task<CreateTableCommand> ParseRequest(CreateTableCommand command)
        {
            if (command == null)
            {
                return Task.FromResult(new CreateTableCommand { TableName = "BadRequest", Response = "Request cannot be empty" });
            }

            if (string.IsNullOrEmpty(command.TableName) || command.TableDefinitions == null || command.TableDefinitions.Length == 0)
            {
                return Task.FromResult(new CreateTableCommand { TableName = "BadRequest", Response = "TableName and TableDefinitions cannot be empty" });
            }

            command.Response = "Success";
            return Task.FromResult(command);
        }
        private bool IsValidIdentifier(string identifier)
        {
            // Basic validation to ensure identifier consists only of letters, digits, and underscores, and does not start with a digit
            return !string.IsNullOrWhiteSpace(identifier) &&
                   identifier.All(c => char.IsLetterOrDigit(c) || c == '_') &&
                   !char.IsDigit(identifier[0]);
        }
    }
}
