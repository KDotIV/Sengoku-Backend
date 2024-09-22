using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Models.Leagues
{
    public class OnboardTournamentToLeagueCommand : ICommand
    {
        public required int[] TournamentIds { get; set; }
        public required int LeagueId { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (TournamentIds.Length > 0 && LeagueId > 0) return true;
            return false;
        }
    }
    public class OnboardPlayerToLeagueCommand : ICommand
    {
        public required int[] PlayerIds { get; set; }
        public required int LeagueId { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (PlayerIds.Length > 0 && LeagueId > 0) return true;
            return false;
        }
    }
    public class GetLeaderboardResultsByLeagueId : ICommand
    {
        public required int LeagueId { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (LeagueId > 0) return true;
            return false;
        }
    }
}
