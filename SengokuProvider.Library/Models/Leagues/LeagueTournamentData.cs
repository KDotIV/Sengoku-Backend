namespace SengokuProvider.Library.Models.Leagues
{
    public class LeagueTournamentData
    {
        public required int LeagueId { get; set; }
        public required int TournamentLinkId { get; set; }
        public required string TournamentName { get; set; }
        public required string UrlSlug { get; set; }
        public required int[] PlayerIds { get; set; } = [];
        public required int EntrantsNum { get; set; }
        public required DateTime LastUpdated { get; set; }
        public required DateTime StartTime { get; set; }
        public string[] ViewershipUrls { get; set; } = [];
        public int GameId { get; set; }
    }
}
