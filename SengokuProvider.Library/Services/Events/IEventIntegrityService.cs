using SengokuProvider.Library.Models.Events;

namespace SengokuProvider.Library.Services.Events
{
    public interface IEventIntegrityService
    {
        public Task<List<int>> BeginEventIntegrity();
        public Task<bool> VerifyEventUpdate();
        public Task<List<int>> BeginIntegrityTournamentLinks();
        public Task<bool> VerifyTournamentLinkChange(int linkId);
        public Task<UpdateEventCommand?> CreateUpdateCommand(int currentEvent);
    }
}