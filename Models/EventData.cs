namespace SengokuProvider.API.Models
{
    public class EventData
    {
        public required int Id { get; set; }
        public required string EventName { get; set; }
        public string? EventDescription { get; set; }
        public required int Region { get; set; }
        public Game[]? Games { get; set; }
        public PlayerData[]? PlayerData { get; set; }
        public LegendData[]? LegendData { get; set; }
    }
}
