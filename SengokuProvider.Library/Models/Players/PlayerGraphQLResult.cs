using Newtonsoft.Json;

namespace SengokuProvider.Library.Models.Players
{
    public class PlayerGraphQLResult
    {
        [JsonProperty("player")]
        public required Player Player { get; set; }
    }
    public class Player
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("gamerTag")]
        public required string GamerTag { get; set; }

        [JsonProperty("user")]
        public User? User { get; set; }

        [JsonProperty("sets")]
        public SetList? Sets { get; set; }
    }

    public class User
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("slug")]
        public string? Slug { get; set; }
    }

    public class SetList
    {
        [JsonProperty("nodes")]
        public List<SetNode>? Nodes { get; set; }
    }

    public class SetNode
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("displayScore")]
        public string? DisplayScore { get; set; }

        [JsonProperty("event")]
        public Event? Event { get; set; }
    }

    public class Event
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("tournament")]
        public Tournament? Tournament { get; set; }
    }

    public class Tournament
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("city")]
        public string? City { get; set; }
    }
}
