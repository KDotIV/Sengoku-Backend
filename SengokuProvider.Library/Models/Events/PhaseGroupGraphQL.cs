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
        public int Id { get; set; }
        [JsonProperty("slots")]
        public List<Slot>? Slots { get; set; }
    }

    public class Slot
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;
        [JsonProperty("entrant")]
        public Entrant? Entrant { get; set; }
    }

    public class Entrant
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("name")]
        public string? Name { get; set; }
    }

}
