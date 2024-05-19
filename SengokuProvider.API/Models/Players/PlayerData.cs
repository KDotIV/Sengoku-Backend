using SengokuProvider.API.Models.Events;
using SengokuProvider.API.Models.Legends;

namespace SengokuProvider.API.Models.Players
{
    public class PlayerData
    {
        public required int Id { get; set; }
        public required string PlayerName { get; set; }
        public string? Style { get; set; }
        public required int LinkId { get; set; }
        public List<EventData>? EventData { get; set; }
        public List<LegendData>? LegendData { get; set; }
    }
}
