using Dapper;
using Microsoft.AspNetCore.DataProtection;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Models.Feeds;
using System.Text.Json;

namespace SengokuProvider.Library.Services.Feeds;

public sealed partial class EventFeedStore
{
    public Task<int> DiscoverNewTournaments(CancellationToken ct) => Database(conn => conn.ExecuteScalarAsync<int>(
        new CommandDefinition("SELECT event_feed_capture_pending()", cancellationToken: ct)), ct);

    public Task<bool> PrepareNext(IEventFeedQueryService events, CancellationToken ct) => Database(async conn =>
    {
        // Hold the feed lock until its snapshot and next scheduled time are committed together.
        await using var tx = await conn.BeginTransactionAsync(ct);
        CommandDefinition Cmd(string sql, object? values = null) => new(sql, values, tx, cancellationToken: ct);

        var feed = await conn.QueryFirstOrDefaultAsync<FeedRow>(Cmd($"SELECT {Columns} FROM event_feed_next_due()"));
        if (feed == null) return false;

        var filter = JsonSerializer.Deserialize<EventFeedFilter>(feed.Filters)!;
        int[]? insertedIds = feed.ScheduleMode == "OnInsert"
            ? (await conn.QueryAsync<int>(Cmd("SELECT * FROM event_feed_notification_batch(@id,@limit)", new { id = feed.Id, limit = filter.MaxEvents }))).ToArray()
            : null;
        string? payload = null;
        var status = "Skipped";
        var outcome = "NoMatches";
        try
        {
            var preview = await events.GetUpcomingFeedEvents(filter, DateTime.UtcNow, ct, insertedIds);
            var digest = new EventDigest(feed.Name, preview.Events.Select(e => new DigestEvent(e.Name, e.UrlSlug, e.StartTimeUtc,
                e.IsOnline ? "Online" : string.Join(", ", new[] { e.City, e.State }.Where(s => !string.IsNullOrWhiteSpace(s))), e.Entrants)).ToArray(), preview.HasMore);
            if (digest.Events.Count > 0)
            {
                _ = DiscordEventFeedOutlet.Payload(digest);
                payload = JsonSerializer.Serialize(digest); status = "Pending"; outcome = "Queued";
            }
        }
        // An administrator can retire a selected catalogue entry. Pause this feed without blocking other owners.
        catch (ArgumentException) { status = "Failed"; outcome = "InvalidFiltersOrEventData"; }
        await conn.ExecuteAsync(Cmd("SELECT event_feed_enqueue_v2(@deliveryId,@id,@scheduled,@status,CAST(@payload AS jsonb),@outcome,CAST(@insertedIds AS integer[]))",
            new { deliveryId = Guid.NewGuid(), id = feed.Id, scheduled = feed.NextRunAtUtc, status, payload, outcome, insertedIds }));
        await tx.CommitAsync(ct);
        return true;
    }, ct);

    public async Task<bool> DispatchNext(IEventFeedOutlet outlet, CancellationToken ct)
    {
        // The function atomically claims and commits before any external HTTP request.
        var job = await Database(conn => conn.QueryFirstOrDefaultAsync<DispatchRow>(new CommandDefinition(
            "SELECT * FROM event_feed_claim()", cancellationToken: ct)), ct);
        if (job == null) return false;
        DeliveryResult result;
        try
        {
            var destination = _protector.Unprotect(job.DestinationProtected);
            var digest = JsonSerializer.Deserialize<EventDigest>(job.Payload)!;
            result = await outlet.Send(destination, digest, ct);
        }
        catch (System.Security.Cryptography.CryptographicException) { result = new("SecretUnavailable"); }
        catch (ArgumentException) { result = new("Rejected"); }
        catch (JsonException) { result = new("Rejected"); }
        catch (Exception) { result = new("Unknown"); }

        // Finalize ambiguous cancellation even when shutdown cancelled the caller token.
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await Database(conn => conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT event_feed_complete(@id,@outcome,@messageId,@delay)",
            new { id = job.Id, outcome = result.Status, messageId = result.MessageId, delay = Math.Max(1, result.RetryAfterSeconds) }, cancellationToken: cleanup.Token)), cleanup.Token);
        return true;
    }

    public Task<int> RecoverInterrupted(CancellationToken ct) => Database(conn => conn.ExecuteScalarAsync<int>(
        new CommandDefinition("SELECT event_feed_recover()", cancellationToken: ct)), ct);
}


