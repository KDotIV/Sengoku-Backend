using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Orgs;

namespace SengokuProvider.Library.Services.Orgs
{
    public interface IOrganizerQueryService
    {
        public Task<List<BracketQueueData>> GetBracketQueueByTournamentId(int tournamentId);
        public Task GetCurrentTournamentStations(int orgId);
        public Task DisplayCurrentBracketQueue(int bracketQueueId);
        public Task<List<TravelCoOpResult>> GetCoOpResultsByUserId(int userId);
    }
}