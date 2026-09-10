using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Models.Legends
{
    public class LegendData
    {
        public int Id { get; set; } = 0;
        public string LegendName { get; set; } = string.Empty;
        public int PlayerId { get; set; } = 0;
        public int PlayerLinkId { get; set; } = 0;
        public string PlayerName { get; set; } = string.Empty;
        public Game[]? Games { get; set; }
        public List<int>? Standings { get; set; }
    }
}
