using Newtonsoft.Json;

namespace SengokuProvider.Library.Models.Players
{
    public class PastEventPlayerData
    {
        [JsonProperty("player")]
        public PlayerQuery? PlayerQuery { get; set; }
    }

    public class PlayerQuery
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("gamerTag")]
        public string? GamerTag { get; set; }

        [JsonProperty("user")]
        public PastEventUser? User { get; set; }
    }

    public class PastEventUser
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("events")]
        public PreviousEvents? PreviousEvents { get; set; }
    }

    public class PreviousEvents
    {
        [JsonProperty("nodes")]
        public List<PreviousEventNode>? Nodes { get; set; }
    }

    public class PreviousEventNode : BaseNode
    {
        [JsonProperty("tournament")]
        public PreviousTournament? PreviousTournament { get; set; }

        [JsonProperty("entrants")]
        public PreviousEntrantList? Entrants { get; set; }
    }

    public class PreviousTournament : BaseTournament { }

    public class PreviousEntrantList
    {
        [JsonProperty("nodes")]
        public List<PreviousEntrantNode>? Nodes { get; set; }
    }

    public class PreviousEntrantNode
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("participants")]
        public List<PreviousParticipant>? Participants { get; set; }

        [JsonProperty("standing")]
        public PreviousStanding? Standing { get; set; }
    }

    public class PreviousParticipant : BaseParticipant { }

    public class PreviousStanding : BaseStanding { }
}
