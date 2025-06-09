using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Models.Players
{
    public class GetRegisteredPlayersByTournamentIdCommand : ICommand
    {
        public required int TournamentLink { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (TournamentLink > 0) return true;
            return false;
        }
    }
    public class OnboardBracketRunnerByBracketSlug : ICommand
    {
        public required string BracketSlug { get; set; }
        public required int PlayerId { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }
        public bool Validate()
        {
            if (!string.IsNullOrEmpty(BracketSlug) && PlayerId > 0)
                return true;
            return false;
        }
    }
    public class IntakePlayersByTournamentCommand : ICommand
    {
        public required int TournamentLink { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (TournamentLink > 0)
                return true;
            return false;
        }
    }
    public class OnboardPlayerDataCommand : ICommand
    {
        public required int PlayerId { get; set; }
        public required string GamerTag { get; set; }
        public int PerPage { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            return true;
        }
    }
    public class IntakePlayerDataCommand : ICommand
    {
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            throw new NotImplementedException();
        }
    }
    public class GetPlayerStandingsCommand : ICommand
    {
        public required int PlayerId { get; set; }
        public string? PlayerName { get; set; }
        public string? Filter { get; set; }
        public string? Sort { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (PlayerId > 0) return true;
            else return false;
        }
    }
}
