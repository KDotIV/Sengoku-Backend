using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Regions;

namespace SengokuProvider.Library.Services.Events
{
    public interface IEventQueryService
    {
        public Task<List<AddressEventResult>> QueryEventsByLocation(GetTournamentsByLocationCommand command, int pageNumber = 5);
        public Task<PlayerStandingResult> QueryPlayerStandings(GetPlayerStandingsCommand command);
        public Task<EventGraphQLResult?> QueryStartggEventByEventId(int eventId);
        public Task<AddressData> GetAddressById(int addressId);
        public Task<RegionData?> QueryRegion(GetRegionCommand command);
    }
}
