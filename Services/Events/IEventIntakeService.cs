using SengokuProvider.API.Models.Events;

namespace SengokuProvider.API.Services.Events
{
    public interface IEventIntakeService
    {
        public Task<Tuple<int, int>> IntakeTournamentData(IntakeEventsByLocationCommand intakeCommand);
        public Task<int> IntakeEventsByGameId(IntakeEventsByGameIdCommand intakeCommand);
        public Task<bool> IntakeEventsByTournamentId(int tournamentId);
    }
}
