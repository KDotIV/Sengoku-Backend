namespace SengokuProvider.Library.Models.Leagues
{
    public class LeaderboardData
    {
        public required int PlayerId { get; set; }
        public required int LeagueId { get; set; }
        public required string PlayerName { get; set; }
        public required int GainedPoints { get; set; }
        public required int TournamentId { get; set; }
        public required string UrlSlug { get; set; }
        public string LeagueName { get; set; } = "";
        public int CurrentScore { get; set; }
        public int ScoreChange { get; set; }
        public int TournamentCount { get; set; }
        public int GameId { get; set; }
        public required DateTime LastUpdated { get; set; }
    }
}
