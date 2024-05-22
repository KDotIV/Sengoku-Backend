using Newtonsoft.Json;

namespace SengokuProvider.Library.Models.Players
{
    public class PlayerGraphQLResult
    {
        [JsonProperty("event")]
        public required EventContainer Data { get; set; }
    }
    public class EventContainer
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public required string Name { get; set; }

        [JsonProperty("entrants")]
        public required EntrantList Entrants { get; set; }
    }

    public class EntrantList
    {
        [JsonProperty("nodes")]
        public required List<EntrantNode> Nodes { get; set; }
    }

    public class EntrantNode
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("participants")]
        public required List<Participant> Participants { get; set; }

        [JsonProperty("standing")]
        public Standing? Standing { get; set; }
    }

    public class Participant
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("player")]
        public required Player Player { get; set; }
    }

    public class Player
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("gamerTag")]
        public required string GamerTag { get; set; }
    }

    public class Standing
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("placement")]
        public int Placement { get; set; }
    }
}
