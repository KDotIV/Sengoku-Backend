namespace SengokuProvider.Library.Models.Leagues
{
    public class LeagueRegionsResult
    {
        public required int LeagueId { get; set; }
        public required string[] Regions { get; set; }
        public required int GameId { get; set; }
    }
}
