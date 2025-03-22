namespace SengokuProvider.Library.Models.Leagues
{
    public class TournamentSchedulerResult
    {
        public required Dictionary<string, string> Success { get; set; }
        public required Dictionary<string, string> Errors { get; set; }
    }
}