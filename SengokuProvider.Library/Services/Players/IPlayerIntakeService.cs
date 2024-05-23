using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Services.Players
{
    public interface IPlayerIntakeService
    {
        public Task<int> IntakePlayerData(IntakePlayersByTournamentCommand command);
    }
}
