namespace SengokuProvider.Library.Models.Events
{
    public class BoardRunnerResult
    {
        public required List<TournamentBoardResult> TournamentList { get; set; } = new List<TournamentBoardResult>();
        public required int UserId { get; set; }
        public required int OrgId { get; set; } = 0;
        public required string Response { get; set; } = string.Empty;
    }
}
