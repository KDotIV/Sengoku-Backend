namespace SengokuProvider.API.Models.Events
{
    public class PlayerStandingResult
    {
        public int Standing { get; set; }
        public string? GamerTag {  get; set; }
        public EventDetails? EventDetails { get; set; }
        public Links? TournamentLinks { get; set; }
        public int EntrantsNum { get; set; }
        public required string Response { get; set; }
    }
    public class EventDetails
    {
        public int EventId { get; set; }
        public string? EventName { get; set; }
        public int TournamentId { get; set; }
        public string? TournamentName { get; set; }
    }
    public class Links
    {
        public int EntrantId { get; set; }
        public int StandingId { get; set; }
    }
}