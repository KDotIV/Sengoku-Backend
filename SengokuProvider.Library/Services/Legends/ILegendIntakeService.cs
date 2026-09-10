using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Legends;
using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Services.Legends
{
    public interface ILegendIntakeService
    {
        public Task<LeagueOnboardResult> AddPlayerToLeague(int[] playerIds, int leagueId);
        public Task<TournamentOnboardResult> AddTournamentToLeague(int[] tournamentIds, int leagueId);
        public Task<UpdateLeaderboardResponse> UpdateLeaderboardStandingsByLeagueId(int[] leagueIds);
        public Task<LeagueByOrgResults> InsertNewLeagueByOrg(int orgId, string leagueName, DateTime startDate, DateTime endDate, int gameId = 0, string description = "");
        public Task<int> InsertNewLegendData(LegendData newLegend);
        public Task<int> InsertNewLegendData(List<LegendData> legendData);
        public Task<LegendData> BuildLegendData(StandingsQueryResult standings, string playerName);
        public Task<List<TournamentBoardResult>> AddTournamentsToRunnerBoard(int userId, int orgId, List<int> tournamentIds);
    }
}
