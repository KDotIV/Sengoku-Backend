namespace SengokuProvider.API.Models
{
    public class PlayerData
    {
        public required int Id { get; set; }
        public required string PlayerName { get; set; }
        public string? Summary { get; set; }
        public EventData[]? EventData { get; set; }
        public LegendData[]? LegendData { get; set; }
    }
}
