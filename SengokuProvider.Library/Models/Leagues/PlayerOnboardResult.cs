namespace SengokuProvider.Library.Models.Leagues
{
    public class PlayerOnboardResult
    {
        public List<int> Successful { get; set; } = new List<int>();
        public List<int> Failures { get; set; } = new List<int>();
        public required string Response { get; set; }
    }
}