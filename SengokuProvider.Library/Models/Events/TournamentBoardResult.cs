namespace SengokuProvider.Library.Models.Events
{
    public class TournamentBoardResult
    {
        public required int TournamentId { get; set; }
        public required string TournamentName { get; set; }
        public required string UrlSlug { get; set; }
        public required int EntrantsNum { get; set; }
        public required DateTime LastUpdated { get; set; }
        public int GameId { get; set; } = 0;
    }
}
