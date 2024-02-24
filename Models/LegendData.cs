namespace SengokuProvider.API.Models
{
    public class LegendData
    {
        public required int Id { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }
        public Game game { get; set; }
        public PlayerData[]? playerData { get; set; }
        public EventData[]? eventData { get; set; }

    }

    public enum Game
    {
        StreetFighter = 1,
        GuiltyGear = 2,
        MarvelVsCapcom = 3,
        None = 0
    }
}
