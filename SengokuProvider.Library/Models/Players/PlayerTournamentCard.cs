namespace SengokuProvider.Library.Models.Players
{
    public class PlayerTournamentCard
    {
        public required int PlayerID { get; set; }
        public required string PlayerName { get; set; }
        public required List<PlayerStandingResult> PlayerResults { get; set; } = new List<PlayerStandingResult>();
    }
}
