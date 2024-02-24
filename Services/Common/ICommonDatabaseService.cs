using SengokuProvider.API.Models.Common;

namespace SengokuProvider.API.Services.Common
{
    public interface ICommonDatabaseService
    {
        public Task<int> CreateTable(string tableName, Tuple<string, string>[] columnDefinitions);
        public Task<CreateTableCommand> ParseRequest(HttpRequest req);
    }
}