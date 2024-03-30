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

        public Task<int> CreateAssociativeTable()
        {
            throw new NotImplementedException();
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
        public Task<T> ParseRequest<T>(T command) where T : ICommand
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command), "Request cannot be empty");
            }

            if (!command.Validate())
            {
                command.Response = "BadRequest: Validation failed";
                return Task.FromResult(command);
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
