using Newtonsoft.Json;

namespace SengokuProvider.Library.Models.User
{
    public class UserGraphQLResult
    {
        [JsonProperty("user")]
        public CommonUserNode UserNode { get; set; } = new CommonUserNode();
    }
}
