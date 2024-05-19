namespace SengokuProvider.Library.Services.Events
{
    public interface IEventIntegrityService
    {
        public Task<List<int>> BeginIntegrityTournamentLinks();
        public Task<bool> VerifyTournamentLinkChange(int linkId);
    }
}