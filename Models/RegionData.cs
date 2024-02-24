namespace SengokuProvider.API.Models
{
    public class RegionData
    {
        public required int Id { get; set; }
        public required string RegionName { get; set; }
        public PlayerData[]? PlayerData { get; set; }
        public EventData[]? EventData { get; set; }
        public LegendData[]? LegendData { get; set; }
    }
}
