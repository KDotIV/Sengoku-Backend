using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;

namespace SengokuProvider.Library.Services.Events
{
    public interface IEventIntakeService
    {
        public Task<List<int>> IntakeTournamentData(IntakeEventsByLocationCommand intakeCommand);
        public Task<int> IntakeTournamentIdData(IntakeEventsByTournamentIdCommand command);
        public Task<int> IntakeEventsByGameId(IntakeEventsByGameIdCommand intakeCommand);
        public Task<bool> SendTournamentIntakeEventMessage(int eventId);
        public Task<bool> SendEventIntakeLocationMessage(IntakeEventsByLocationCommand command);
        public Task<bool> UpdateEventData(UpdateEventCommand command);
        public Task<int> IntakeNewRegion(AddressData addressData);
    }
}
