using Newtonsoft.Json;

namespace SengokuProvider.Library.Models.Players
{
    public class BaseNode
    {
        [JsonProperty("id")]
        public int Id { get; set; }
        [JsonProperty("name")]
        public string? Name { get; set; }
        [JsonProperty("numEntrants")]
        public int? NumEntrants { get; set; }
        [JsonProperty("slug")]
        public required string Slug { get; set; }
    }
    public class BaseTournament
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string? Name { get; set; }
    }
    public class BaseParticipant
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("player")]
        public Player? Player { get; set; }

        [JsonProperty("user")]
        public User? User { get; set; }
    }
    public class BaseStanding
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("placement")]
        public int Placement { get; set; }
        [JsonProperty("isFinal")]
        public bool IsActive { get; set; }
    }
}