using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Legends;

namespace SengokuProvider.Library.Services.Legends
{
    public interface ILegendIntakeService
    {
        public Task<PlayerOnboardResult> AddPlayerToLeague(int[] playerIds, int leagueId);
        public Task<TournamentOnboardResult> AddTournamentToLeague(int[] tournamentIds, int leagueId);
        public Task<LeagueByOrgResults> CreateLeagueByOrg(int orgId, string leagueName, DateTime startDate, DateTime endDate, int gameId = 0, string description = "");
        public Task<LegendData?> GenerateNewLegends(int playerId, string playerName);
        public Task<int> InsertNewLegendData(LegendData newLegend);
    }
}