using System.Text.Json;

namespace SengokuProvider.Library.Models.Contracts
{
    public sealed record IngestionJobEnvelope(
        Guid MessageId,
        int SchemaVersion,
        string CorrelationId,
        IngestionJobType JobType,
        DateTimeOffset EnqueuedAtUtc,
        JsonElement Payload)
    {
        public const int CurrentSchemaVersion = 1;

        public static IngestionJobEnvelope Create<T>(IngestionJobType jobType, T payload, string? correlationId = null)
        {
            return new IngestionJobEnvelope(
                Guid.NewGuid(),
                CurrentSchemaVersion,
                correlationId ?? Guid.NewGuid().ToString("N"),
                jobType,
                DateTimeOffset.UtcNow,
                JsonSerializer.SerializeToElement(payload));
        }

        public T ReadPayload<T>() =>
            Payload.Deserialize<T>() ?? throw new InvalidOperationException($"Unable to deserialize {JobType} payload as {typeof(T).Name}.");
    }

    public enum IngestionJobType
    {
        ImportEventsByLocation = 1,
        ImportEventsByGame = 2,
        LinkTournamentByEvent = 3,
        ImportPlayersByTournament = 4,
        ImportPlayerHistory = 5,
        BuildBracketSnapshot = 6,
        LinkStartGgUser = 7,
        ImportTournamentStandingsToLeague = 8
    }
}
