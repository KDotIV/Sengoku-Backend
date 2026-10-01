using SengokuProvider.Library.Models.Leagues;

namespace SengokuProvider.Library.Models.Players;

public sealed class BracketProcessingCheckpoint
{
    public int SchemaVersion { get; set; } = 1;
    public Guid OperationId { get; set; } = Guid.NewGuid();
    public required string RequestKey { get; set; }
    public int BracketId { get; set; }
    public required BracketVictoryPathData Data { get; set; }
    public List<ExpectedOpponent> ExpectedOpponents { get; set; } = [];
    public int[] MissingPlayerLinks { get; set; } = [];
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
    public PlayerOnboardResult Result { get; set; } = new() { Response = "Waiting for legends", Status = "Pending" };
}
