using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;

namespace SengokuProvider.Library.Services.Events
{
    public interface IEventIntakeService
    {
        public Task<List<int>> IntakeTournamentData(IntakeEventsByLocationCommand intakeCommand);
        public Task<int> IntakeTournamentIdData(LinkTournamentByEventIdCommand command);
        public Task<int> IntakeEventsByGameId(IntakeEventsByGameIdCommand intakeCommand);
        public Task<bool> SendTournamentLinkEventMessage(int eventLinkId);
        public Task<bool> SendEventIntakeLocationMessage(IntakeEventsByLocationCommand command);
        public Task<bool> UpdateEventData(UpdateEventCommand command);
        public Task<int> IntakeNewRegion(AddressData addressData);
    }
}
