
namespace SengokuProvider.Library.Models.Players
{
    public class PlayerTournamentCard
    {
        public required int PlayerID { get; set; }
        public required string PlayerName { get; set; }
        public int EntrantID { get; set; } = 0;
        public required List<PlayerStandingResult> PlayerResults { get; set; } = new List<PlayerStandingResult>();
    }
    public class EntrantSetCard
    {
        public required int EntrantOneID { get; set; }
        public int PlayerOneID { get; set; }
        public required string EntrantOneName { get; set; }
        public required int EntrantTwoID { get; set; }
        public int PlayerTwoID { get; set; }
        public required string EntrantTwoName { get; set; }
        public int? PathStep { get; set; }
        public string? PathSetId { get; set; }
        public string MatchStatus { get; set; } = "Candidate";
        public required string SetID { get; set; }
    }
}
