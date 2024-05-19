using SengokuProvider.Library.Models.Events;

namespace SengokuProvider.Library.Services.Events
{
    public interface IEventIntakeService
    {
        public Task<Tuple<int, int>> IntakeTournamentData(IntakeEventsByLocationCommand intakeCommand);
        public Task<int> IntakeEventsByGameId(IntakeEventsByGameIdCommand intakeCommand);
        public Task<bool> IntakeEventsByTournamentId(int tournamentId);
    }
}
