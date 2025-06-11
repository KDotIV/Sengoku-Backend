using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Regions;

namespace SengokuProvider.Library.Services.Events
{
    public interface IEventQueryService
    {
        public Task<List<AddressEventResult>> GetEventsByLocation(GetTournamentsByLocationCommand command, int pageNumber = 5);
        public Task<List<TournamentData>> GetTournamentLinksById(int[] tournamentLinkId);
        public Task<EventGraphQLResult?> QueryStartggEventByEventId(int eventId);
        public Task<AddressData> GetAddressById(int addressId);
        public Task<RegionData?> QueryRegion(GetRegionCommand command);
        public Task<List<string>> QueryRelatedRegionsById(string regionId);
        public Task<List<string>> QueryLocalRegionsById(string regionId);
        public Task<List<TournamentData>> GetTournamentLinksByUrl(string eventLinkSlug, int[]? gameIds = default);
        public Task<TournamentData> GetTournamentLinkbyUrl(string tournamentLinkSlug);
        public Task<TournamentsBySlugGraphQLResult?> QueryStartggTournamentLinksByUrl(string eventLinkSlug);
        public Task<List<AddressEventResult>> GetLocalEventsByLeagueRegions(int[] leagueIds, string[] regions, int page);
        public Task<List<TournamentData>> GetTournamentsByEventIds(int[] eventIds);
        public Task<List<TournamentData>> GetTournamentsByLeagueIds(int[] leagueIds);
    }
}
