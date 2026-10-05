using Dapper;
using Newtonsoft.Json;
using Npgsql;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Worker.Handlers;

namespace SengokuProvider.Library.Services.Players;

public sealed class BracketCheckpointStore(string connectionString) : IBracketCheckpointStore
{
    public async Task<BracketProcessingCheckpoint?> GetAsync(Guid operationId)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            var payload = await connection.QuerySingleOrDefaultAsync<string>(
                "SELECT payload::text FROM bracket_processing_checkpoints WHERE operation_id = @operationId", new { operationId });
            return payload == null ? null : JsonConvert.DeserializeObject<BracketProcessingCheckpoint>(payload);
        }
        catch (NpgsqlException ex)
        {
            var errorMessage = $"Database error while reading bracket checkpoint {operationId} (SQLSTATE {ex.SqlState}): {ex.Message}";
            Console.Error.WriteLine($"{errorMessage}{Environment.NewLine}{ex}");
            throw new ApplicationException(errorMessage, ex);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error while reading bracket checkpoint {operationId}: {ex}");
            throw;
        }
    }

    public async Task<BracketProcessingCheckpoint> ExecuteAsync(string requestKey,
        Func<BracketProcessingCheckpoint?, NpgsqlConnection, NpgsqlTransaction, Task<BracketProcessingCheckpoint>> action)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            // Serializes initial requests as well as resumes across API/worker replicas.
            await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@requestKey, 0))", new { requestKey }, transaction);

            var payload = await connection.QuerySingleOrDefaultAsync<string>(
                "SELECT payload::text FROM bracket_processing_checkpoints WHERE request_key = @requestKey", new { requestKey }, transaction);

            var checkpoint = await action(payload == null ? null : JsonConvert.DeserializeObject<BracketProcessingCheckpoint>(payload), connection, transaction);
            checkpoint.Result.OperationId = checkpoint.OperationId;

            await connection.ExecuteAsync("""
            INSERT INTO bracket_processing_checkpoints (operation_id, request_key, payload, status, expires_at, updated_at)
            VALUES (@OperationId, @RequestKey, CAST(@payload AS jsonb), @status, @ExpiresAt, now())
            ON CONFLICT (request_key) DO UPDATE SET payload = EXCLUDED.payload,
                status = EXCLUDED.status, updated_at = now()
            """, new
            {
                checkpoint.OperationId,
                checkpoint.RequestKey,
                payload = JsonConvert.SerializeObject(checkpoint),
                status = checkpoint.Result.Status,
                checkpoint.ExpiresAt
            }, transaction);

            if (checkpoint.Result.Status != "Pending")
                await connection.ExecuteAsync("DELETE FROM bracket_processing_outbox WHERE operation_id = @OperationId AND sent_at IS NULL",
                    new { checkpoint.OperationId }, transaction);

            await transaction.CommitAsync();
            return checkpoint;
        }
        catch (NpgsqlException ex)
        {
            var errorMessage = $"Database error while saving bracket checkpoint {requestKey} (SQLSTATE {ex.SqlState}): {ex.Message}";
            Console.Error.WriteLine($"{errorMessage}{Environment.NewLine}{ex}");
            throw new ApplicationException(errorMessage, ex);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error while processing bracket checkpoint {requestKey}: {ex}");
            throw;
        }
    }

    public async Task EnqueueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid operationId,
        string queue, object message, DateTime availableAt)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(queue)) throw new InvalidOperationException("Bracket processing queue is not configured.");
            await connection.ExecuteAsync("""
                INSERT INTO bracket_processing_outbox (id, operation_id, queue_name, payload, available_at)
                VALUES (@id, @operationId, @queue, CAST(@payload AS jsonb), @availableAt)
                """, new { id = Guid.NewGuid(), operationId, queue, payload = JsonConvert.SerializeObject(message), availableAt }, transaction);
        }
        catch (NpgsqlException ex)
        {
            var errorMessage = $"Database error while enqueuing bracket operation {operationId} for queue {queue} (SQLSTATE {ex.SqlState}): {ex.Message}";
            Console.Error.WriteLine($"{errorMessage}{Environment.NewLine}{ex}");
            throw new ApplicationException(errorMessage, ex);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error while enqueuing bracket operation {operationId} for queue {queue}: {ex}");
            throw;
        }
    }

    public async Task EnqueueResumeAsync(Guid operationId, string queue)
    {
        var checkpoint = await GetAsync(operationId);
        if (checkpoint == null) return;

        await ExecuteAsync(checkpoint.RequestKey, async (current, connection, transaction) =>
        {
            if (current == null) throw new InvalidOperationException("Checkpoint was removed.");
            if (current.Result.Status == "Pending")
                await EnqueueAsync(connection, transaction, operationId, queue,
                    new PlayerReceivedData { Command = new ResumeBracketProcessingCommand { OperationId = operationId },
                        MessagePriority = MessagePriority.SystemIntake }, DateTime.UtcNow);
            return current;
        });
    }
}
