namespace SengokuProvider.Library.Models.Players
{
    public class BracketVictoryPathData
    {
        public required int TournamentLinkID { get; set; }
        public required int EventLinkID { get; set; }
        public string TournamentName { get; set; } = string.Empty;
        public string RoundNum { get; set; } = string.Empty;
        public required PlayerTournamentCard PlayerTournamentCard { get; set; }
        public required List<EntrantSetCard> EntrantSetCards { get; set; } = new List<EntrantSetCard>();
    }
}
