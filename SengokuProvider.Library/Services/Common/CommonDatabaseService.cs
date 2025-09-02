using Dapper;
using Npgsql;
using SengokuProvider.Library.Services.Common.Interfaces;
using System.Diagnostics;
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
        public string[] SanitizeStringArray(string[] input, bool trimQuotes = true)
        {
            if (input == null || input.Length == 0)
                return Array.Empty<string>();

            return input.Select(r =>
            {
                var cleaned = r.Trim();
                return trimQuotes ? cleaned.Trim('\'') : cleaned;
            }).ToArray();
        }
        public async Task<T> MeasureExecutionTimeAsync<T>(Func<Task<T>> func, string operationName)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var result = await func();
                sw.Stop();
                Console.WriteLine($"{operationName} executed in {sw.ElapsedMilliseconds}ms");
                return result;
            }
            catch (Exception)
            {
                sw.Stop();
                Console.WriteLine($"{operationName} failed after {sw.ElapsedMilliseconds}ms");
                throw;
            }
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
