using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Legends;

namespace SengokuProvider.Library.Services.Legends
{
    public interface ILegendIntakeService
    {
        public Task<TournamentOnboardResult> AddTournamentToLeague(int tournamentId, int leagueId);
        public Task<LegendData?> GenerateNewLegends(int playerId, string playerName);
        public Task<int> InsertNewLegendData(LegendData newLegend);
    }
}