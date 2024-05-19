using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Models.Legends
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
