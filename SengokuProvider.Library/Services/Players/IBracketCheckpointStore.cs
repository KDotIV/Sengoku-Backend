using Npgsql;
using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Services.Players;

public interface IBracketCheckpointStore
{
    Task<BracketProcessingCheckpoint?> GetAsync(Guid operationId);
    Task<BracketProcessingCheckpoint> ExecuteAsync(string requestKey,
        Func<BracketProcessingCheckpoint?, NpgsqlConnection, NpgsqlTransaction, Task<BracketProcessingCheckpoint>> action);
    Task EnqueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid operationId,
        string queue, object message, DateTime availableAt);
    Task EnqueueResumeAsync(Guid operationId, string queue);
}
