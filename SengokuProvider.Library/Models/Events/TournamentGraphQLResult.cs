using Newtonsoft.Json;

namespace SengokuProvider.Library.Models.Events
{
    public class TournamentsBySlugGraphQLResult
    {
        [JsonProperty("tournament")]
        public required EventLinkResult Event { get; set; }
    }
    public class TournamentLinkGraphQLResult
    {
        [JsonProperty("event")]
        public required TournamentDetails TournamentLink { get; set; }
    }

    public class EventLinkResult
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("name")]
        public string? Name { get; set; }
        [JsonProperty("nodes")]
        public required List<EventNode> Nodes { get; set; }
    }
}
