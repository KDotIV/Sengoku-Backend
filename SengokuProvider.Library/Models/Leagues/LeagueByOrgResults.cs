namespace SengokuProvider.Library.Models.Leagues
{
    public class LeagueByOrgResults
    {
        public required int LeagueId { get; set; } = 0;
        public required string LeagueName { get; set; } = "N/A";
        public required int OrgId { get; set; } = 0;
        public required DateTime StartDate { get; set; } = DateTime.MinValue;
        public required DateTime EndDate { get; set; } = DateTime.MinValue.AddDays(1);
        public int Game { get; set; }
        public required DateTime LastUpdate { get; set; } = DateTime.UtcNow;
        public required bool IsActive { get; set; } = false;
        public string Response { get; set; } = "";
    }
}
