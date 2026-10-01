namespace SengokuProvider.Library.Models.Leagues
{
    public class PlayerOnboardResult
    {
        public List<string> Successful { get; set; } = new List<string>();
        public List<string> Failures { get; set; } = new List<string>();
        public required string Response { get; set; }
        public Guid? OperationId { get; set; }
        public string? Status { get; set; }
    }
}
