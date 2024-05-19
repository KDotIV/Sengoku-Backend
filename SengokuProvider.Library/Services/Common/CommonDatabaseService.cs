using Dapper;
using Npgsql;

namespace SengokuProvider.Library.Services.Common
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
        private bool IsValidIdentifier(string identifier)
        {
            return !string.IsNullOrWhiteSpace(identifier) &&
                   identifier.All(c => char.IsLetterOrDigit(c) || c == '_') &&
                   !char.IsDigit(identifier[0]);
        }
    }
}
