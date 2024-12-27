namespace SengokuProvider.Library.Models.Leagues
{
    public class LeaderboardData
    {
        public required int PlayerId { get; set; }
        public required int LeagueId { get; set; }
        public required string PlayerName { get; set; }
        public required int CurrentScore { get; set; }
        public int ScoreDifference { get; set; }
        public int TournamentCount { get; set; }
    }
}
