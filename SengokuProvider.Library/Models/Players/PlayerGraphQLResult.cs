using Newtonsoft.Json;

namespace SengokuProvider.Library.Models.Players
{
    public class PlayerGraphQLResult
    {
        [JsonProperty("event")]
        public CommonEventNode TournamentLink { get; set; } = new CommonEventNode();
    }
}
