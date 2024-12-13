using GraphQL.Client.Http;
using SengokuProvider.Library.Models.Orgs;
using SengokuProvider.Library.Services.Common;

namespace SengokuProvider.Library.Services.Orgs
{
    public class OrganizerIntakeService : IOrganizerIntakeService
    {
        private readonly string _connectionString;
        private readonly GraphQLHttpClient _client;
        private readonly RequestThrottler _throttler;
        public OrganizerIntakeService(string connectionString, GraphQLHttpClient client, RequestThrottler throttler)
        {
            _connectionString = connectionString;
            _client = client;
            _throttler = throttler;
        }

        public Task AddBracketToRun(int[] tournamentIds, int userId)
        {
            throw new NotImplementedException();
        }

        public Task<bool> CreateTravelCoOp(CreateTravelCoOpCommand command)
        {
            throw new NotImplementedException();
        }

        public Task DeleteBracketFromCurrentRun(int[] tournamentIds, int userId)
        {
            throw new NotImplementedException();
        }
        public async Task StartBracketRun(int[] tournamentIds, int userId)
        {
            if (tournamentIds.Length == 0 || userId == 0) return;
        }

        public Task UpdateBracketFromCurrentRun(int[] tournamentIds, int userId)
        {
            throw new NotImplementedException();
        }
    }
}
