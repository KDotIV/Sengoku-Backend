using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Legends;

namespace SengokuProvider.Library.Services.Legends
{
    public interface ILegendIntakeService
    {
        public Task<PlayerOnboardResult> AddPlayerToLeague(int[] playerIds, int leagueId);
        public Task<TournamentOnboardResult> AddTournamentToLeague(int[] tournamentIds, int leagueId);
        public Task<UpdateLeaderboardResponse> UpdateLeaderboardStandingsByLeagueId(int[] leagueIds);
        public Task<LeagueByOrgResults> InsertNewLeagueByOrg(int orgId, string leagueName, DateTime startDate, DateTime endDate, int gameId = 0, string description = "");
        public Task<LegendData?> GenerateNewLegends(int playerId, string playerName);
        public Task<int> InsertNewLegendData(LegendData newLegend);
        public Task<BoardRunnerResult> CreateNewRunnerBoard(List<int> tournamentIds, int userId, string userName, int orgId = default, string? orgName = default);
        public Task<List<TournamentBoardResult>> AddTournamentsToRunnerBoard(int userId, int orgId, List<int> tournamentIds);
        public Task<bool> AddLeagueToUser(int leagueId, int userId);
        public Task<LeaderboardOnboardIntakeResult> IntakeTournamentStandingsByEventLink(int[] tournamentLinks, string eventLinkSlug, int[] gameIds, int leagueId, bool open = false);
    }
}