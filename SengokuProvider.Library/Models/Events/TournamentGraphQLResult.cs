using Newtonsoft.Json;

namespace SengokuProvider.Library.Models.Events
{
    public class TournamentGraphQLResult
    {
        [JsonProperty("tournament")]
        public required EventLinkResult Event { get; set; }
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
