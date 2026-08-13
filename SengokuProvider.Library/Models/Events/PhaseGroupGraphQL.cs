using Newtonsoft.Json;

namespace SengokuProvider.Library.Models.Events
{
    public class PhaseGroupGraphQL
    {
        [JsonProperty("phaseGroup")]
        public PhaseGroup? PhaseGroup { get; set; }
    }

    public class PhaseGroup
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("displayIdentifier")]
        public string? DisplayIdentifier { get; set; }
        [JsonProperty("sets")]
        public Sets Sets { get; set; }
    }

    public class Sets
    {
        [JsonProperty("pageInfo")]
        public PageInfo? PageInfo { get; set; }
        [JsonProperty("nodes")]
        public List<SetNode>? Nodes { get; set; }
    }

    public class SetNode
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("identifier")]
        public string Identifier { get; set; } = string.Empty;

        [JsonProperty("round")]
        public int Round { get; set; }

        [JsonProperty("fullRoundText")]
        public string FullRoundText { get; set; } = string.Empty;

        [JsonProperty("state")]
        public int State { get; set; }

        [JsonProperty("winnerId")]
        public int? WinnerId { get; set; }
        [JsonProperty("slots")]
        public List<Slot>? Slots { get; set; }
    }

    public class Slot
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("prereqId")]
        public string? PrereqId { get; set; }

        [JsonProperty("prereqType")]
        public string? PrereqType { get; set; }

        [JsonProperty("prereqPlacement")]
        public int? PrereqPlacement { get; set; }
        [JsonProperty("entrant")]
        public Entrant? Entrant { get; set; }
    }

    public class Entrant
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("name")]
        public string? Name { get; set; }
        [JsonProperty("standing")]
        public Standing? Standing { get; set; }
        [JsonProperty("participants")]
        public List<Participant>? Participants { get; set; } = new List<Participant>();
    }
    public class Standing
    {
        [JsonProperty("player")]
        public Player? Player { get; set; }
    }
    public class Player
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("gamerTag")]
        public string GamerTag { get; set; } = string.Empty;
    }
    public class Participant
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("player")]
        public Player? Player { get; set; }
    }
}
