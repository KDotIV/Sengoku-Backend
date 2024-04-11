using SengokuProvider.API.Models.Common;
using SengokuProvider.API.Models.Legends;
using SengokuProvider.API.Models.Players;

namespace SengokuProvider.API.Models.Events
{
    public class EventData
    {
        public required int Id { get; set; }
        public required string EventName { get; set; }
        public string? EventDescription { get; set; }
        public required int Region { get; set; }
        public List<Game>? Games { get; set; }
        public List<PlayerData>? PlayerData { get; set; }
        public List<LegendData>? LegendData { get; set; }
    }
}
