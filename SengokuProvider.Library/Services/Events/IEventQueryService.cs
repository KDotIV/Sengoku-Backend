using SengokuProvider.Library.Models.Events;

namespace SengokuProvider.Library.Services.Events
{
    public interface IEventQueryService
    {
        public Task<List<AddressEventResult>> QueryEventsByLocation(GetTournamentsByLocationCommand command, int pageNumber = 5);
        public Task<PlayerStandingResult> QueryPlayerStandings(GetPlayerStandingsCommand command);
    }
}
