namespace SengokuProvider.Library.Models.Players
{
    public class StandingsQueryResult
    {
        public required int PlayerID { get; set; }
        public required List<StandingsResult> StandingData { get; set; }
    }

    public class StandingsResult
    {
        public required int EntrantID { get; set; }
        public required int TournamentLink { get; set; }
        public required int Placement { get; set; }
        public required int EntrantsNum { get; set; }
        public required bool IsActive { get; set; }
    }
}
