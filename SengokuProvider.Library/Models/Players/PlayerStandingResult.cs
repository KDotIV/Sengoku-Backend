namespace SengokuProvider.Library.Models.Players
{
    public class PlayerStandingResult
    {
        public StandingDetails? StandingDetails { get; set; }
        public int LeaugeId { get; set; }
        public Links? TournamentLinks { get; set; }
        public int EntrantsNum { get; set; }
        public string? Response { get; set; }
        public string? UrlSlug { get; set; }
        public required DateTime LastUpdated { get; set; }
    }
    public class StandingDetails
    {
        public bool IsActive { get; set; }
        public int Placement { get; set; }
        public int LeaguePoints { get; set; }
        public string? GamerTag { get; set; }
        public int EventId { get; set; }
        public string? EventName { get; set; }
        public int TournamentId { get; set; }
        public string? TournamentName { get; set; }
    }
    public class Links
    {
        public required int PlayerId { get; set; }
        public required int EntrantId { get; set; }
        public int StandingId { get; set; }
    }
}