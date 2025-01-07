using Dapper;
using Npgsql;
using SengokuProvider.Library.Services.Common.Interfaces;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SengokuProvider.Library.Services.Common
{
    public class CommonDatabaseService : ICommonDatabaseService
    {
        private readonly string? _connectionString;

        public CommonDatabaseService(string? connectionString)
        {
            _connectionString = connectionString;
        }
        public Task<int> CreateAssociativeTable()
        {
            throw new NotImplementedException();
        }
        public NpgsqlParameter CreateDBIntArrayType(string parameterName, int[] array)
        {
            var newParameters = new NpgsqlParameter();
            newParameters.ParameterName = parameterName;
            newParameters.Value = array ?? Array.Empty<int>();
            newParameters.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer;

            return newParameters;
        }
        public NpgsqlParameter CreateDBTextArrayType(string parameterName, string[] array)
        {
            var newParameters = new NpgsqlParameter();
            newParameters.ParameterName = parameterName;
            newParameters.Value = array;
            newParameters.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text;

            return newParameters;
        }
        public NpgsqlParameter CreateDBNumericType(string parameterName, double value)
        {
            var newParameters = new NpgsqlParameter();
            newParameters.ParameterName = parameterName;
            newParameters.Value = value;
            newParameters.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Numeric;

            return newParameters;
        }
        public string CleanUrlSlugName(string urlSlug)
        {
            if (string.IsNullOrEmpty(urlSlug))
                return string.Empty;

            //Extract the tournament name part
            var tournamentMatch = Regex.Match(urlSlug, @"tournament/([^/]+)/event");
            var tournamentPart = tournamentMatch.Success ? tournamentMatch.Groups[1].Value : string.Empty;

            //Extract the event name part
            var eventMatch = Regex.Match(urlSlug, @"event/([^/]+)");
            var eventPart = eventMatch.Success ? eventMatch.Groups[1].Value : string.Empty;

            //Clean and capitalize both parts
            var cleanedTournamentPart = CleanAndCapitalize(tournamentPart);
            var cleanedEventPart = CleanAndCapitalize(eventPart);

            // Combine both parts into one result string
            return $"{cleanedTournamentPart} {cleanedEventPart}".Trim();
        }
        private string CleanAndCapitalize(string input)
        {
            // Remove special characters except '#', keep A-Z, a-z, 0-9
            var cleanedInput = Regex.Replace(input, @"[^A-Za-z0-9#\s]", " ");

            var textInfo = CultureInfo.CurrentCulture.TextInfo;
            return textInfo.ToTitleCase(cleanedInput.ToLower());
        }
        public async Task<int> CreateTable(string tableName, Tuple<string, string>[] columnDefinitions)
        {
            if (!IsValidIdentifier(tableName) || columnDefinitions.Any(cn => !IsValidIdentifier(cn.Item1)) || columnDefinitions == null)
            {
                throw new ArgumentException("Invalid table or column name.");
            }

            var columnsDefinition = string.Join(", ", columnDefinitions.Select((cn, index) => $"@colName{index} {cn.Item2}"));
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                var createTableCommand = @$"CREATE TABLE IF NOT EXISTS @tableName (
                    ""id"" SERIAL PRIMARY KEY,
                    {columnsDefinition}
                    );";

                var command = new NpgsqlCommand(createTableCommand, conn);
                command.Parameters.AddWithValue("@tableName", tableName);
                for (int i = 0; i < columnDefinitions.Length; i++)
                {
                    command.Parameters.AddWithValue($"@colName{i}", columnDefinitions[i].Item1);
                }

                return await command.ExecuteNonQueryAsync();
            }
        }
        private bool IsValidIdentifier(string identifier)
        {
            return !string.IsNullOrWhiteSpace(identifier) &&
                   identifier.All(c => char.IsLetterOrDigit(c) || c == '_') &&
                   !char.IsDigit(identifier[0]);
        }
    }
    public class GenericArrayHandler<T> : SqlMapper.TypeHandler<T[]>
    {
        public override T[]? Parse(object value) => (T[])value;

        public override void SetValue(System.Data.IDbDataParameter parameter, T[]? value)
        {
            parameter.Value = value;
        }
    }
}
