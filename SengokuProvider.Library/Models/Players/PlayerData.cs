namespace SengokuProvider.Library.Models.Players
{
    public class PlayerData
    {
        public required int Id { get; set; }
        public required string PlayerName { get; set; }
        public string? Style { get; set; }
        public required int PlayerLinkID { get; set; }
        public int UserId { get; set; }
        public required int UserLink { get; set; }
        public string? LegendCheckSum { get; set; }
        public required DateTime LastUpdate { get; set; }
    }
}
