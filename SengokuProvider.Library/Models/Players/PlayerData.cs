using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Legends;

namespace SengokuProvider.Library.Models.Players
{
    public class PlayerData
    {
        public required int Id { get; set; }
        public required string PlayerName { get; set; }
        public string? Summary { get; set; }
        public List<EventData>? EventData { get; set; }
        public List<LegendData>? LegendData { get; set; }
    }
}
