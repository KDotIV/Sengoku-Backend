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
    public class FlatBracketPathEntrantCards
    {
        public int BracketPathId { get; set; }
        public int TournamentLink { get; set; }
        public string TournamentName { get; set; } = string.Empty;
        public int EventLink { get; set; }
        public string RoundNum { get; set; } = string.Empty;
        public int PlayerId { get; set; }
        public int SetId { get; set; }
        public int PlayerOneId { get; set; }
        public int PlayerTwoId { get; set; }
        public string PlayerOneName { get; set; } = string.Empty;
        public string PlayerTwoName { get; set; } = string.Empty;
        public int EntrantOneId { get; set; }
        public int EntrantTwoId { get; set; }
    }
}
