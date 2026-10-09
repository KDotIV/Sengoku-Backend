namespace SengokuProvider.Library.Models.Players
{
    public class BracketVictoryPathData
    {
        public int BracketPathId { get; set; }
        public int? BracketId { get; set; }
        public int PlayerStartggLink { get; set; }
        public string? TournamentSlug { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Lifecycle { get; set; } = "Unknown";
        public required int TournamentLinkID { get; set; }
        public required int EventLinkID { get; set; }
        public string TournamentName { get; set; } = string.Empty;
        public string RoundNum { get; set; } = string.Empty;
        public required PlayerTournamentCard PlayerTournamentCard { get; set; }
        public required List<EntrantSetCard> EntrantSetCards { get; set; } = new List<EntrantSetCard>();
    }
    public sealed record ExpectedOpponent(int EntrantId, int PlayerLink, string GamerTag, string pathSetIdentifier, string SourceSetIdentifier)
    {
        public int? PathStep { get; init; }
        public string PathSetId { get; init; } = string.Empty;
    }
}
