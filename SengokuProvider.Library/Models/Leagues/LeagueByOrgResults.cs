namespace SengokuProvider.Library.Models.Leagues
{
    public class LeagueByOrgResults
    {
        public required int LeagueId { get; set; }
        public required string LeagueName { get; set; }
        public required int OrgId { get; set; }
        public required DateTime StartDate { get; set; }
        public required DateTime EndDate { get; set; }
        public int Game { get; set; }
        public required DateTime LastUpdate { get; set; }
        public string Response { get; set; } = "";
    }
}
