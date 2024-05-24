using Newtonsoft.Json;

namespace SengokuProvider.Library.Models.Players
{
    public class PlayerGraphQLResult : CommonPlayerSchema
    {

    }
    public class Player
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("gamerTag")]
        public required string GamerTag { get; set; }
    }
}
