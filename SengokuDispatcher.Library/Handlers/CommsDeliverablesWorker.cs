using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Dapper;
using Npgsql;
using SengokuDispatcher.Library.Workflows.Events;

namespace SengokuDispatcher.Library.Handlers;

public sealed class CommsDeliverablesWorker(IConfiguration config,
    ILogger<CommsDeliverablesWorker> log) : BackgroundService
{
    private sealed record Change(Guid Id, string Entity, int EntityId, int? EditionId, string Operation, DateTime OccurredAt)
    {
        [System.Text.Json.Serialization.JsonRequired]
        public int SchemaVersion { get; init; } = 1;
    }

    private NpgsqlConnection OpenDatabase() => new(config.GetConnectionString("AlexandriaConnectionString"));

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        const string queue = "commsdeliverablesqueue";
        var connection = config["ServiceBusSettings:CommsDeliverablesConnection"]
            ?? throw new InvalidOperationException("Configure the commsdeliverablesqueue connection in Dispatcher.");
        await using var client = new ServiceBusClient(connection);
        await using var sender = client.CreateSender(queue);
        await using var processor = client.CreateProcessor(queue, new ServiceBusProcessorOptions { AutoCompleteMessages = false });
        processor.ProcessMessageAsync += Consume;
        processor.ProcessErrorAsync += args =>
        {
            log.LogError("Comms queue processing failed ({ErrorType}).", args.Exception.GetType().Name);
            return Task.CompletedTask;
        };
        await processor.StartProcessingAsync(ct);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (await PublishBatch(sender, ct)) continue;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                { log.LogError("Comms outbox publish failed ({ErrorType}).", ex.GetType().Name); }
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        finally { await processor.StopProcessingAsync(CancellationToken.None); }
    }

    private async Task<bool> PublishBatch(ServiceBusSender sender, CancellationToken ct)
    {
        await using var conn = OpenDatabase();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var changes = (await conn.QueryAsync<Change>(new CommandDefinition("""
            SELECT id,entity,entity_id AS EntityId,edition_id AS EditionId,operation,occurred_at AS OccurredAt
            FROM comms_change_outbox ORDER BY occurred_at,id LIMIT 50 FOR UPDATE SKIP LOCKED
            """, transaction: tx, cancellationToken: ct))).ToArray();
        if (changes.Length == 0) return false;
        var messages = changes.Select(change => new ServiceBusMessage(BinaryData.FromObjectAsJson(change))
        {
            MessageId = change.Id.ToString(), ContentType = "application/json", Subject = change.Entity
        });
        await sender.SendMessagesAsync(messages, ct);
        await conn.ExecuteAsync(new CommandDefinition("DELETE FROM comms_change_outbox WHERE id=ANY(@ids)",
            new { ids = changes.Select(c => c.Id).ToArray() }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return true;
    }

    private async Task Consume(ProcessMessageEventArgs args)
    {
        Change? change;
        try { change = args.Message.Body.ToObjectFromJson<Change>(); }
        catch (JsonException) { change = null; }
        if (change is null || change.SchemaVersion != 1 || change.Id == Guid.Empty ||
            change.EntityId <= 0 || change.OccurredAt.Kind != DateTimeKind.Utc ||
            change.Operation is not ("INSERT" or "UPDATE") ||
            change.Entity is not ("events" or "tournament_links" or "major_circuit_calendar" or "players" or "legends" or "leagues"))
        {
            await args.DeadLetterMessageAsync(args.Message, "InvalidChange", cancellationToken: args.CancellationToken);
            return;
        }
        if (change.Entity is "events" or "tournament_links" or "major_circuit_calendar")
        {
            await using var conn = OpenDatabase();
            await conn.ExecuteAsync(new CommandDefinition("SELECT event_feed_accept_change(@EditionId,@OccurredAt)",
                new { change.EditionId, change.OccurredAt }, cancellationToken: args.CancellationToken));
        }
        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }
}

// Use the existing event-intake queue for start.gg refreshes. Unlinked editions are observed via calendar edits.
public sealed class MajorCalendarRefreshWorker(IConfiguration config, IServiceScopeFactory scopes,
    ILogger<MajorCalendarRefreshWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(config.GetConnectionString("AlexandriaConnectionString"));
                await conn.OpenAsync(ct);
                await using var tx = await conn.BeginTransactionAsync(ct);
                var id = await conn.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
                    SELECT c.startgg_id FROM major_circuit_calendar c LEFT JOIN events e ON e.link_id=c.startgg_id
                    WHERE c.active AND c.startgg_id IS NOT NULL AND c.refresh_after<=now()
                      AND coalesce(e.end_time,c.end_utc,e.start_time,c.start_utc)>=timezone('UTC',now())
                    ORDER BY c.refresh_after,c.id LIMIT 1 FOR UPDATE OF c SKIP LOCKED
                    """, transaction: tx, cancellationToken: ct));
                if (id.HasValue)
                {
                    using var scope = scopes.CreateScope();
                    var intake = scope.ServiceProvider.GetRequiredService<IEventOperations>();
                    if (!await intake.SendTournamentLinkEventMessage(id.Value))
                        throw new InvalidOperationException("Calendar refresh enqueue failed.");
                    await conn.ExecuteAsync(new CommandDefinition("""
                        UPDATE major_circuit_calendar SET refresh_after=now()+interval '6 hours' WHERE startgg_id=@id
                        """, new { id }, tx, cancellationToken: ct));
                }
                await tx.CommitAsync(ct);
                if (id.HasValue) continue;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { log.LogError("Calendar refresh failed ({ErrorType}).", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }
}
