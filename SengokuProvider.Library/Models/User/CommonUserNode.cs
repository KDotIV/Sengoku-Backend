using Newtonsoft.Json;

namespace SengokuProvider.Library.Models.User
{
    public class CommonUserNode
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("name")]
        public string? Name { get; set; }
        [JsonProperty("slug")]
        public string? Slug { get; set; }
        [JsonProperty("player")]
        public Player Player { get; set; } = new Player();
    }
    public class Player
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("gamerTag")]
        public string? GamerTag { get; set; }
    }
}