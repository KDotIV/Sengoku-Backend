using Newtonsoft.Json;

namespace SengokuProvider.API.Models.Events
{
    public class StandingGraphQLResult
    {
        [JsonProperty("event")]
        public required Event Event { get; set; }
    }

    public class Event
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
        public List<Participant>? Participants { get; set; }

        [JsonProperty("standing")]
        public Standing? Standing { get; set; }
    }

    public class Participant
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("gamerTag")]
        public string? GamerTag { get; set; }
    }

    public class Standing
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("placement")]
        public int Placement { get; set; }

        [JsonProperty("container")]
        public Container? Container { get; set; }
    }

    public class Container
    {
        [JsonProperty("__typename")]
        public string? Typename { get; set; }

        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("startAt")]
        public long StartAt { get; set; }

        [JsonProperty("numEntrants")]
        public int NumEntrants { get; set; }

        [JsonProperty("tournament")]
        public Tournament? Tournament { get; set; }
    }

    public class Tournament
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string? Name { get; set; }
    }
}
