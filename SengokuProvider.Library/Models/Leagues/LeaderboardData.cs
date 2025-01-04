namespace SengokuProvider.Library.Models.Leagues
{
    public class LeaderboardData
    {
        public required int PlayerId { get; set; }
        public required int LeagueId { get; set; }
        public required string PlayerName { get; set; }
        public required int CurrentScore { get; set; }
        public string LeagueName { get; set; } = "";
        public int ScoreDifference { get; set; }
        public int TournamentCount { get; set; }
        public int GameId { get; set; }
        public required DateTime LastUpdated { get; set; }
    }
}
