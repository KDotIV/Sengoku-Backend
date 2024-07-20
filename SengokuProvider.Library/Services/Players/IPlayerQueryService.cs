using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Services.Players
{
    public interface IPlayerQueryService
    {
        public Task<PlayerGraphQLResult?> GetPlayerDataFromStartgg(IntakePlayersByTournamentCommand queryCommand);
        public Task<PlayerStandingResult?> QueryStartggPlayerStandings(GetPlayerStandingsCommand command);
        public Task<PastEventPlayerData?> QueryStartggPreviousEventData(OnboardPlayerDataCommand queryCommand);
        public Task<List<PlayerStandingResult>> GetPlayerStandingResults(QueryPlayerStandingsCommand command);
    }
}
