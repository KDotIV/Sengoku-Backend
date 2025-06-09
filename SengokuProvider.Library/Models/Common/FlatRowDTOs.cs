namespace SengokuProvider.Library.Models.Common
{
    public class FlatPlayerStandings
    {
            public int PlayerID { get; set; }
            public string PlayerName { get; set; } = string.Empty;
            public int EntrantId { get; set; }
            public int Placement { get; set; }
            public int Tournament_Link { get; set; }
            public int EntrantsNum { get; set; }
            public DateTime LastUpdated { get; set; }
    }
}
