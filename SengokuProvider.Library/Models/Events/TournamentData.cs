namespace SengokuProvider.Library.Models.Events
{
    public class TournamentData
    {
        public required int Id { get; set; }
        public required string UrlSlug { get; set; }
        public int GameId { get; set; }
        public string[]? SocialLinks { get; set; }
        public string? MatcherinoSlug { get; set; }
        public string[]? ViewershipUrls { get; set; }
        public int[]? PlayerIDs { get; set; }
        public required int EventId { get; set; }
        public int EntrantsNum { get; set; }
        public required DateTime LastUpdated { get; set; }
    }
}
