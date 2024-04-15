using SengokuProvider.API.Models.Events;

namespace SengokuProvider.API.Services.Events
{
    public interface IEventService
    {
        public Task<Tuple<int, int>> IntakeTournamentData(TournamentIntakeCommand intakeCommand);
    }
}
