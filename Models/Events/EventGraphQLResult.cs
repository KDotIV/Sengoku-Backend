using Newtonsoft.Json;

namespace SengokuProvider.API.Models.Events
{
    public class EventGraphQLResult
    {
        [JsonProperty("tournaments")]
        public required TournamentResult Tournaments { get; set; }
    }

    public class TournamentResult
    {
        [JsonProperty("nodes")]
        public required List<EventNode> Nodes { get; set; }
    }

    public class EventNode
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public required string Name { get; set; }

        [JsonProperty("addrState")]
        public required string AddrState { get; set; }

        [JsonProperty("lat")]
        public double Lat { get; set; }

        [JsonProperty("lng")]
        public double Lng { get; set; }

        [JsonProperty("venueAddress")]
        public required string VenueAddress { get; set; }

        [JsonProperty("startAt")]
        public long StartAt { get; set; }

        [JsonProperty("endAt")]
        public long EndAt { get; set; }
    }
}
