using SengokuProvider.API.Models.Common;
using SengokuProvider.API.Models.Events;
using SengokuProvider.API.Models.Players;

namespace SengokuProvider.API.Models.Legends
{
    public class LegendData
    {
        public required int Id { get; set; }
        public required string Name { get; set; }
        public string? Description { get; set; }
        public Game game { get; set; }
        public List<PlayerData>? playerData { get; set; }
        public List<EventData>? eventData { get; set; }

    }
}
