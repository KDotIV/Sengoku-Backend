namespace SengokuProvider.Library.Models.Leagues
{
    public class LeaderboardOnboardIntakeResult
    {
        public required LeagueOnboardResult PlayerResult { get; set; }
        public required TournamentOnboardResult TournamentResults { get; set; }
    }
}