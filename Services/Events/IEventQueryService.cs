using SengokuProvider.API.Models.Events;

namespace SengokuProvider.API.Services.Events
{
    public interface IEventQueryService
    {
        public Task<List<AddressEventResult>> QueryEventsByLocation(GetTournamentsByLocationCommand command, int pageNumber = 5);
        public Task<PlayerStandingResult> QueryPlayerStandingsByEventId(GetPlayerStandingsByEventIdCommand command);
    }
}
