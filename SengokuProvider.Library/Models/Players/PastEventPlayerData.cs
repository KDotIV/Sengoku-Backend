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
        public User? User { get; set; }
    }

    public class User
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("events")]
        public PreviousEvents? PreviousEvents { get; set; }
    }

    public class PreviousEvents
    {
        [JsonProperty("nodes")]
        public List<PreviousNodes>? Nodes { get; set; }
    }

    public class PreviousNodes
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string? Name { get; set; }
    }
}
