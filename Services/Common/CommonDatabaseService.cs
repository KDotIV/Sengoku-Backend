using Dapper;
using Newtonsoft.Json;
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
                    ""Id"" SERIAL PRIMARY KEY,
                    {columnsDefinition}
                    );";

                return await conn.ExecuteAsync(createTableCommand);
            }
        }
        public async Task<CreateTableCommand> ParseRequest(HttpRequest req)
        {
            if (req == null) return new CreateTableCommand { TableName = "BadRequest", Response = "Request cannot be empty" };

            using Stream bodyStream = req.BodyReader.AsStream();

            using (var streamReader = new StreamReader(bodyStream))
            {
                var requestData = await streamReader.ReadToEndAsync();

                try
                {
                    var serializedResult = JsonConvert.DeserializeObject<CreateTableCommand>(requestData);

                    if (serializedResult != null)
                    {
                        serializedResult.Response = "Success";
                        return serializedResult;
                    }
                }
                catch (Exception ex)
                {
                    return new CreateTableCommand { TableName = "BadRequest", Response = ex.Message };
                }
            }

            return new CreateTableCommand { TableName = "BadRequest", Response = "Failed to parse request" };
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
