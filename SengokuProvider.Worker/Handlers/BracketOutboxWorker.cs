using Azure.Messaging.ServiceBus;
using Dapper;
using Npgsql;
using SengokuProvider.Library.Workflows.Players;
using SengokuProvider.Library.Services.Players;

namespace SengokuProvider.Worker.Handlers;

/// <summary>Publishes committed workflow commands. A crash after send may replay a
/// message; consumers must remain idempotent even if broker deduplication is enabled.</summary>
internal sealed class BracketOutboxWorker(IConfiguration configuration, ServiceBusClient bus,
    IPlayerOperations players, IBracketCheckpointStore checkpoints, ILogger<BracketOutboxWorker> log) : BackgroundService
{
    private readonly string _connectionString = configuration.GetConnectionString("AlexandriaConnectionString")
        ?? throw new InvalidOperationException("Database connection is not configured.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(stoppingToken);
                var expired = await connection.QueryAsync<Guid>("""
                    SELECT operation_id FROM bracket_processing_checkpoints
                    WHERE status = 'Pending' AND expires_at <= now() LIMIT 100
                    """);
                foreach (var id in expired) await players.ResumeBracketProcessing(id);
                // Recover a lost/dead-lettered resume notification. EnqueueResume
                // updates updated_at, bounding recovery notifications to one per
                // five minutes per operation even when consumers are offline.
                var stalled = await connection.QueryAsync<Guid>("""
                    SELECT c.operation_id FROM bracket_processing_checkpoints c
                    WHERE c.status = 'Pending' AND c.expires_at > now()
                        AND c.updated_at < now() - interval '5 minutes'
                        AND NOT EXISTS (SELECT 1 FROM bracket_processing_outbox o
                            WHERE o.operation_id = c.operation_id AND o.sent_at IS NULL)
                    LIMIT 100
                    """);
                foreach (var id in stalled)
                    await checkpoints.EnqueueResumeAsync(id, configuration["ServiceBusSettings:PlayerReceivedQueue"]!);
                for (var i = 0; i < 50 && !stoppingToken.IsCancellationRequested; i++)
                {
                    await using var transaction = await connection.BeginTransactionAsync(stoppingToken);
                    var message = await connection.QuerySingleOrDefaultAsync<OutboxMessage>("""
                        SELECT id AS Id, operation_id AS OperationId, queue_name AS QueueName,
                            payload::text AS Payload, attempts AS Attempts
                        FROM bracket_processing_outbox
                        WHERE sent_at IS NULL AND available_at <= now()
                        ORDER BY available_at LIMIT 1 FOR UPDATE SKIP LOCKED
                        """, transaction: transaction);
                    if (message == null) break;
                    try
                    {
                        await using var sender = bus.CreateSender(message.QueueName);
                        await sender.SendMessageAsync(new ServiceBusMessage(message.Payload)
                        {
                            MessageId = message.Id.ToString(), CorrelationId = message.OperationId.ToString(),
                            ContentType = "application/json"
                        }, stoppingToken);
                        await connection.ExecuteAsync("UPDATE bracket_processing_outbox SET sent_at = now(), last_error = NULL WHERE id = @Id",
                            new { message.Id }, transaction);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        log.LogWarning(ex, "Could not publish bracket message {MessageId}; retrying", message.Id);
                        await connection.ExecuteAsync("""
                            UPDATE bracket_processing_outbox SET attempts = attempts + 1,
                                last_error = @error, available_at = now() + make_interval(secs => @delay)
                            WHERE id = @Id
                            """, new { message.Id, error = ex.Message,
                                delay = Math.Min(300, 5 * Math.Pow(2, Math.Min(message.Attempts, 6))) }, transaction);
                    }
                    await transaction.CommitAsync(stoppingToken);
                }
                await connection.ExecuteAsync("""
                    DELETE FROM bracket_processing_outbox WHERE sent_at < now() - interval '7 days';
                    DELETE FROM bracket_processing_outbox o USING bracket_processing_checkpoints c
                        WHERE o.operation_id = c.operation_id AND c.expires_at < now() - interval '7 days';
                    DELETE FROM bracket_processing_checkpoints WHERE expires_at < now() - interval '7 days';
                    """);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Bracket outbox cycle failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private sealed class OutboxMessage
    {
        public Guid Id { get; set; }
        public Guid OperationId { get; set; }
        public string QueueName { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public int Attempts { get; set; }
    }
}
