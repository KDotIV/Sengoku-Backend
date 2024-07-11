using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Services.Players
{
    public interface IPlayerIntakeService
    {
        public Task<int> IntakePlayerData(IntakePlayersByTournamentCommand command);
        public Task<bool> SendPlayerIntakeMessage(string eventSlug, int perPage = 50, int pageNum = 5);
    }
}
