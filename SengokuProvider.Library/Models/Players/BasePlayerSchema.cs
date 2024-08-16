using Newtonsoft.Json;

namespace SengokuProvider.Library.Models.Players
{
    public class CommonPlayer
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("gamerTag")]
        public string GamerTag { get; set; } = string.Empty;
        [JsonProperty("user")]
        public CommonUser User { get; set; } = new CommonUser();
    }

    public class CommonUser
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("events")]
        public CommonEvents Events { get; set; } = new CommonEvents();
    }

    public class CommonEvents
    {
        [JsonProperty("nodes")]
        public List<CommonEventNode> Nodes { get; set; } = new List<CommonEventNode>();

        [JsonProperty("pageInfo")]
        public PageInfo? PageInfo { get; set; }
    }

    public class CommonEventNode : BaseNode
    {
        [JsonProperty("tournament")]
        public CommonTournament EventLink { get; set; } = new CommonTournament();

        [JsonProperty("entrants")]
        public CommonEntrantList Entrants { get; set; } = new CommonEntrantList();

        [JsonProperty("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonProperty("numEntrants")]
        public int NumEntrants { get; set; } = 0;
    }

    public class CommonTournament : BaseTournament { }

    public class CommonEntrantList
    {
        [JsonProperty("nodes")]
        public List<CommonEntrantNode> Nodes { get; set; } = new List<CommonEntrantNode>();
        [JsonProperty("pageInfo")]
        public PageInfo? PageInfo { get; set; }
    }

    public class CommonEntrantNode : BaseNode
    {
        [JsonProperty("participants")]
        public List<CommonParticipant> Participants { get; set; } = new List<CommonParticipant>();

        [JsonProperty("standing")]
        public CommonStanding Standing { get; set; } = new CommonStanding();

        [JsonProperty("paginatedSets")]
        public CommonSetList SetList { get; set; } = new CommonSetList();
    }

    public class CommonParticipant : BaseParticipant { }

    public class CommonStanding : BaseStanding { }

    public class CommonSetList
    {
        [JsonProperty("nodes")]
        public List<CommonSet> Nodes { get; set; } = new List<CommonSet>();
    }

    public class CommonSet
    {
        [JsonProperty("round")]
        public int Round { get; set; }

        [JsonProperty("winnerId")]
        public int WinnerEntrantId { get; set; }
    }

    public class PageInfo
    {
        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("totalPages")]
        public int TotalPages { get; set; }

        [JsonProperty("page")]
        public int Page { get; set; }

        [JsonProperty("perPage")]
        public int PerPage { get; set; }
    }

    public class BaseNode
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class BaseTournament
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("slug")]
        public string Slug { get; set; } = string.Empty;

        [JsonProperty("numEntrants")]
        public int NumEntrants { get; set; } = new int();
    }

    public class BaseParticipant
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("player")]
        public CommonPlayer Player { get; set; } = new CommonPlayer();

        [JsonProperty("user")]
        public CommonUser User { get; set; } = new CommonUser();
    }

    public class BaseStanding
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("placement")]
        public int Placement { get; set; } = new int();

        [JsonProperty("isFinal")]
        public bool IsActive { get; set; }
    }
}