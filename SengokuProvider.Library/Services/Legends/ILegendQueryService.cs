using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Legends;
using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Services.Legends
{
    public interface ILegendQueryService
    {
        public Task<List<TournamentBoardResult>> GetCurrentRunnerBoard(int userId, int orgId = 0);
        public Task<List<LeaderboardData>> GetLeaderboardResultsByLeagueId(int leagueId);
        public Task<List<LeaderboardData>> GetCurrentLeaderBoardResults(int[] leagueIds, int[] playerIds);
        public Task<List<LeagueByOrgResults>> GetLeaderboardsByOrgId(int OrgId);
        public Task<LegendData> GetLegendByPlayerIds(List<int> playerID);
        public Task<LegendData?> GetLegendsByPlayerLink(GetLegendsByPlayerLinkCommand getLegendsByPlayerLinkCommand);
        public Task<StandingsQueryResult?> QueryStandingsByPlayerId(int playerId);
    }
}