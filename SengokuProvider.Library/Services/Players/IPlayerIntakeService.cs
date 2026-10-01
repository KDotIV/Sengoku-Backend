using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Services.Players
{
    public interface IPlayerIntakeService
    {
        Task<PlayerOnboardResult> SaveVictoryPathData(BracketVictoryPathData processedData);
        Task<PlayerOnboardResult> SaveVictoryPathData(BracketVictoryPathData processedData, Npgsql.NpgsqlConnection connection, Npgsql.NpgsqlTransaction transaction);
        Task<int> IntakePlayerStandingData(List<PlayerStandingResult> currentStandings);
        Task<int> InsertNewPlayerData(List<PlayerData> players);
    }
}
