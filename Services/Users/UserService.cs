using SengokuProvider.API.Models.Common;
using SengokuProvider.API.Services.Common;

namespace SengokuProvider.API.Services.Users
{
    public class UserService : ICommonDatabaseService, IUserService
    {
        public Task<int> CreateAssociativeTable()
        {
            throw new NotImplementedException();
        }

        public Task<int> CreateTable(string tableName, Tuple<string, string>[] columnDefinitions)
        {
            throw new NotImplementedException();
        }

        public Task<CreateTableCommand> ParseRequest(CreateTableCommand command)
        {
            throw new NotImplementedException();
        }
    }
}
