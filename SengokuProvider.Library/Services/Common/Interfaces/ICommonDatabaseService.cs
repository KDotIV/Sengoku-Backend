using Npgsql;

namespace SengokuProvider.Library.Services.Common.Interfaces
{
    public interface ICommonDatabaseService
    {
        public Task<int> CreateTable(string tableName, Tuple<string, string>[] columnDefinitions);
        public Task<int> CreateAssociativeTable();
        public NpgsqlParameter CreateDBIntArrayType(string parameterName, int[] array);
        public NpgsqlParameter CreateDBTextArrayType(string parameterName, string[] array);
        public string CleanUrlSlugName(string urlSlug);
    }
}