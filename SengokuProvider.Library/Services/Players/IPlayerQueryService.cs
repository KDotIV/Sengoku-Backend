using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Services.Players
{
    public interface IPlayerQueryService
    {
        public Task<PlayerGraphQLResult> GetPlayerDataFromStartgg(IntakePlayersByTournamentCommand queryCommand);

    }
}
