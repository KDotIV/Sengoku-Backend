namespace SengokuProvider.Library.Services.Common
{
    public interface ICommonDatabaseService
    {
        public Task<int> CreateTable(string tableName, Tuple<string, string>[] columnDefinitions);
        public Task<int> CreateAssociativeTable();
    }
}