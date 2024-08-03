using Newtonsoft.Json;

namespace SengokuProvider.Library.Models.Players
{
    public class PlayerGraphQLResult
    {
        [JsonProperty("event")]
        public required Event Data { get; set; }
    }

    public class Event : BaseNode
    {
        [JsonProperty("tournament")]
        public required Tournament Tournament { get; set; }

        [JsonProperty("entrants")]
        public required EntrantList Entrants { get; set; }
    }

    public class EntrantList
    {
        [JsonProperty("nodes")]
        public required List<EntrantNode> Nodes { get; set; }

        [JsonProperty("pageInfo")]
        public PageInfo? PageInfo { get; set; }
    }

    public class EntrantNode : BaseNode
    {
        [JsonProperty("participants")]
        public required List<Participant> Participants { get; set; }

        [JsonProperty("standing")]
        public required Standing Standing { get; set; }
    }

    public class Participant : BaseParticipant { }

    public class Standing : BaseStanding { }

    public class Tournament : BaseTournament { }

    public class Player
    {
        [JsonProperty("id")]
        public required int Id { get; set; }

        [JsonProperty("gamerTag")]
        public required string GamerTag { get; set; }
    }

    public class User
    {
        [JsonProperty("id")]
        public required int Id { get; set; }
    }

    public class PageInfo
    {
        [JsonProperty("total")]
        public required int Total { get; set; }

        [JsonProperty("totalPages")]
        public required int TotalPages { get; set; }

        [JsonProperty("page")]
        public required int Page { get; set; }

        [JsonProperty("perPage")]
        public required int PerPage { get; set; }

        [JsonProperty("sortBy")]
        public string? SortBy { get; set; }

        [JsonProperty("filter")]
        public string? Filter { get; set; }
    }
}
