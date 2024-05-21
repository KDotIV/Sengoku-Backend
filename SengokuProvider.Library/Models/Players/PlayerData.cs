namespace SengokuProvider.Library.Models.Players
{
    public class PlayerData
    {
        public required int Id { get; set; }
        public required string GamerTag { get; set; }
        public string? Style { get; set; }
        public required int PlayerLinkID { get; set; }
        public required int UserLinkID { get; set; }
        public required int LegendID { get; set; }
    }
}
