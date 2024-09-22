namespace SengokuProvider.Library.Models.Leagues
{
    public class LeaderboardData
    {
        public required string PlayerName { get; set; }
        public required int TotalPoints { get; set; }
        public required int TournamentCount { get; set; }
    }
}
