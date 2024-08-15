using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Legends;

namespace SengokuProvider.Library.Services.Legends
{
    public interface ILegendIntakeService
    {
        public Task<PlayerOnboardResult> AddPlayerToLeague(int playerId, int leagueId);
        public Task<TournamentOnboardResult> AddTournamentToLeague(int[] tournamentIds, int leagueId);
        public Task<LegendData?> GenerateNewLegends(int playerId, string playerName);
        public Task<int> InsertNewLegendData(LegendData newLegend);
    }
}