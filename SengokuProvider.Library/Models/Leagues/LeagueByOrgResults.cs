namespace SengokuProvider.Library.Models.Leagues
{
    public class LeagueByOrgResults
    {
        public required int Id { get; set; }
        public required string Name { get; set; }
        public required int OrgId { get; set; }
        public required DateTime StartDate { get; set; }
        public required DateTime EndDate { get; set; }
        public int Game { get; set; }
        public required DateTime LastUpdate { get; set; }
    }
}
