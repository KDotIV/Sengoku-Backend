using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Legends;
using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Workflows.Legends;

public interface ILegendsOperations
{
    Task<List<LegendData>> GenerateNewLegendsByPlayerLinks(int[] playerLinks);
    Task<List<LegendData>> GenerateNewLegendsByPlayerStandings(List<PlayerStandingResult> playerStandings);
    Task<LeaderboardOnboardIntakeResult> IntakeTournamentStandingsByEventLink(int[] tournamentLinks, string eventLinkSlug, int[] gameIds, int leagueId, bool open = false);
    Task<BoardRunnerResult> CreateNewRunnerBoard(List<int> tournamentIds, int userId, string userName, int orgId = default, string? orgName = default);
    Task<string> AddUserToLeague(int playerId, string playerName, string playerEmail, int leagueId, int[] gameIds);
    Task<bool> AddLeagueToUser(int leagueId, int userId);
}
