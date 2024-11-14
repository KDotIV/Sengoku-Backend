using SengokuProvider.Library.Models.Events;

namespace SengokuProvider.Library.Services.Orgs
{
    public interface IOrganizerQueryService
    {
        public Task<List<BracketQueueData>> GetBracketQueueByTournamentId(int tournamentId);
        public Task GetCurrentTournamentStations(int orgId);
        public Task DisplayCurrentBracketQueue(int bracketQueueId);
    }
}