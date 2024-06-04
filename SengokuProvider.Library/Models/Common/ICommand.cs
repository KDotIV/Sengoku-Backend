namespace SengokuProvider.Library.Models.Common
{
    public interface ICommand
    {
        public string? Response { get; set; }
        public CommandRegistry Topic { get; set; }
        bool Validate();
    }
    public enum CommandRegistry
    {
        UpdateEvent,
        IntakeEventsByTournament,
        IntakeEventsByLocation,
        IntakeEventsByGames,
        GetTournamentByLocation
    }
}
