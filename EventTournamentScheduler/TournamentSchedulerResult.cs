namespace EventTournamentScheduler
{
    internal class TournamentSchedulerResult
    {
        public required Dictionary<string, string> Success { get; set; }
        public required Dictionary<string, string> Errors { get; set; }
    }
}
