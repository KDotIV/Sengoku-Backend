﻿using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Models.Legends
{
    public class LegendData
    {
        public required int Id { get; set; }
        public required string LegendName { get; set; }
        public required int PlayerId { get; set; }
        public required int PlayerLinkId { get; set; }
        public required string PlayerName { get; set; }
        public Game[]? Games { get; set; }
        public List<int>? Standings { get; set; }
    }
}
