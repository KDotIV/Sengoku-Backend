using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Services.Players
{
    public interface IPlayerIntakeService
    {
        public Task<int> IntakePlayerData(int tournamentLink);
        public Task<bool> SendPlayerIntakeMessage(int tournamentLink);
        public Task<int> OnboardPreviousTournamentData(OnboardPlayerDataCommand command, int volumeLimit = 100);
        public Task<PlayerOnboardResult> OnboardBracketRunnerByBracketSlug(string bracketSlug, int playerId);
    }
}
