using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Models.Leagues
{
    public class OnboardTournamentToLeagueCommand : ICommand
    {
        public required int TournamentId { get; set; }
        public required int LeagueId { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (TournamentId > 0 && LeagueId > 0) return true;
            return false;
        }
    }
    public class OnboardPlayerToLeagueCommand : ICommand
    {
        public required int PlayerId { get; set; }
        public required int LeagueId { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (PlayerId > 0 && LeagueId > 0) return true;
            return false;
        }
    }
}
