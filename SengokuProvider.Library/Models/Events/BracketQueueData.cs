namespace SengokuProvider.Library.Models.Events
{
    public class BracketQueueData
    {
        public required int BracketQueueId { get; set; }
        public required List<MatchDetails> CurrentMatches { get; set; }
    }
    public class MatchDetails
    {
        public required int LeftPlayerId { get; set; }
        public required string LeftPlayerName { get; set; }
        public required int RightPlayerId { get; set; }
        public required string RightPlayerName { get; set; }
        public required string Station { get; set; }
    }
}