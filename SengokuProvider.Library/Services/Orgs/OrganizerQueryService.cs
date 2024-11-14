using GraphQL.Client.Http;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Services.Common;

namespace SengokuProvider.Library.Services.Orgs
{
    public class OrganizerQueryService : IOrganizerQueryService
    {
        private readonly string _connectionString;
        private readonly GraphQLHttpClient _client;
        private readonly RequestThrottler _throttler;
        public OrganizerQueryService(string connectionString, GraphQLHttpClient httpClient, RequestThrottler throttler)
        {
            _connectionString = connectionString;
            _client = httpClient;
            _throttler = throttler;
        }
        public Task DisplayCurrentBracketQueue(int bracketQueueId)
        {
            throw new NotImplementedException();
        }

        public Task<List<BracketQueueData>> GetBracketQueueByTournamentId(int tournamentId)
        {
            throw new NotImplementedException();
        }

        public Task GetCurrentTournamentStations(int orgId)
        {
            throw new NotImplementedException();
        }
    }
}
