using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Services.Players
{
    public interface IPlayerIntakeService
    {
        public Task<int> IntakePlayerData(IntakePlayersByTournamentCommand command);
        public Task<bool> SendPlayerIntakeMessage(int tournamentLink);
        public Task<int> OnboardPreviousTournamentData(OnboardPlayerDataCommand command, int volumeLimit = 100);
    }
}
