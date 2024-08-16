using Newtonsoft.Json;

namespace SengokuProvider.Library.Models.Players
{
    public class PastEventPlayerData
    {
        [JsonProperty("player")]
        public CommonPlayer PlayerQuery { get; set; } = new CommonPlayer();
    }
}
