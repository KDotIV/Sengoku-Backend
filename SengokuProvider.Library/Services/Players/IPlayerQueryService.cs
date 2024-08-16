using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Services.Players
{
    public interface IPlayerQueryService
    {
        public Task<PlayerGraphQLResult?> QueryPlayerDataFromStartgg(IntakePlayersByTournamentCommand queryCommand);
        public Task<List<PlayerStandingResult>> QueryStartggPlayerStandings(int tournamentLink);
        public Task<PastEventPlayerData> QueryStartggPreviousEventData(int playerId, string gamerTag, int perPage);
        public Task<List<PlayerStandingResult>> GetPlayerStandingResults(GetPlayerStandingsCommand command);
    }
}
