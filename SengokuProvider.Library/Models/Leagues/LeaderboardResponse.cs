namespace SengokuProvider.Library.Models.Leagues
{
    public class LeaderboardResponse
    {
    }
    public class UpdateLeaderboardResponse
    {
        public required List<int> SuccessfulPayers { get; set; } = new List<int>();
        public required List<int> FailedPlayers { get; set; } = new List<int>();
        public required string Message { get; set; }
    }
    public class GetLeaderboardsResponse
    {

    }
}

