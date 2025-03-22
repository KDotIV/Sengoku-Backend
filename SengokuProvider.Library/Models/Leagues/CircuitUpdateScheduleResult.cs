namespace SengokuProvider.Library.Models.Leagues
{
    public class CircuitUpdateScheduleResult
    {
        public required Dictionary<int, string> Success { get; set; }
        public required Dictionary<int, string> Errors { get; set; }
    }
}
