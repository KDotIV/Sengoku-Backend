using Newtonsoft.Json;

namespace SengokuProvider.Library.Models.Players
{
    public class CommonPlayerSchema
    {
        [JsonProperty("event")]
        public required Event Data { get; set; }
    }

    public class Event
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public required string Name { get; set; }

        [JsonProperty("numEntrants")]
        public int NumEntrants { get; set; }

        [JsonProperty("slug")]
        public string? Slug { get; set; }

        [JsonProperty("tournament")]
        public required Tournament Tournament { get; set; }

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

        [JsonProperty("gamerTag")]
        public required string GamerTag { get; set; }
    }

    public class Standing
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("placement")]
        public int Placement { get; set; }

        [JsonProperty("isFinal")]
        public bool IsActive { get; set; }
    }
    public class Tournament
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string? Name { get; set; }
    }

    public class Player
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("gamerTag")]
        public required string GamerTag { get; set; }
    }

    public class User
    {
        [JsonProperty("id")]
        public int Id { get; set; }
    }

}