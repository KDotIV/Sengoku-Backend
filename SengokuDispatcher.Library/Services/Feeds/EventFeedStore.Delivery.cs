using SengokuDispatcher.Library.Models.Feeds;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Models.Feeds;
using System.Text.Json;

namespace SengokuDispatcher.Library.Services.Feeds;

public sealed partial class EventFeedStore
{
    public Task<int> DiscoverNewTournaments(CancellationToken ct) => Database(conn => conn.ExecuteScalarAsync<int>(
        new CommandDefinition("SELECT event_feed_edition_maintenance()", cancellationToken: ct)), ct);

    public Task<bool> PrepareNext(IEventFeedQueryService events, CancellationToken ct) => Database(async conn =>
    {
        // Hold the feed lock until its snapshot and next scheduled time are committed together.
        await using var tx = await conn.BeginTransactionAsync(ct);
        CommandDefinition Cmd(string sql, object? values = null) => new(sql, values, tx, cancellationToken: ct);

        var feed = await conn.QueryFirstOrDefaultAsync<FeedRow>(Cmd($"SELECT {Columns} FROM event_feed_next_edition_due()"));
        if (feed == null) return false;

        var filter = JsonSerializer.Deserialize<EventFeedFilter>(feed.Filters)!;
        var work = feed.ScheduleMode == "OnInsert"
            ? (await conn.QueryAsync<EditionWork>(Cmd("""
                SELECT edition_id AS EditionId, created_at AS CreatedAt FROM event_feed_edition_work
                WHERE subscription_id=@id AND created_at>now()-interval '7 days'
                ORDER BY (edition_id=0) DESC,created_at,edition_id LIMIT @limit FOR UPDATE
                """, new { id = feed.Id, limit = filter.MaxEvents }))).ToArray()
            : [];
        if (feed.ScheduleMode == "OnInsert" && work.Length == 0) return false;
        bool reconnect = work.Any(w => w.EditionId == 0);
        if (reconnect) work = work.Where(w => w.EditionId == 0).ToArray();
        var processed = work.Select(w => w.EditionId).ToArray();
        int[] announced = [];
        bool retainWork = false;
        string? payload = null;
        var status = "Skipped";
        var outcome = "NoMatches";
        try
        {
            // Scheduled digests drain a previous oversized snapshot before starting a new one.
            // Completed deliveries retain only the non-secret overflow cursor, never the message or destination.
            int[] deferred = [];
            if (feed.ScheduleMode != "OnInsert")
            {
                var previous = await conn.QueryFirstOrDefaultAsync<string?>(Cmd("""
                    SELECT CASE WHEN status='Sent' AND (payload->>'SubscriptionVersion')::integer=@version
                        THEN (payload->'DeferredEditionIds')::text END FROM event_feed_deliveries
                    WHERE subscription_id=@id ORDER BY scheduled_for DESC LIMIT 1
                    """, new { id = feed.Id, version = feed.Version }));
                if (previous != null) deferred = JsonSerializer.Deserialize<int[]>(previous) ?? [];
            }
            var preview = feed.ScheduleMode == "OnInsert"
                ? await events.GetEditionFeedEvents(filter, DateTime.UtcNow, reconnect ? null : processed, feed.Id, ct)
                : deferred.Length > 0
                    ? await events.GetEditionFeedEvents(filter, DateTime.UtcNow, deferred, Guid.Empty, ct)
                    : await events.GetUpcomingFeedEvents(filter, DateTime.UtcNow, ct);
            var digest = new EventDigest(feed.Name, preview.Events.Select(e => new DigestEvent(e.Name, e.UrlSlug, e.StartTimeUtc,
                e.IsOnline ? "Online" : string.Join(", ", new[] { e.City, e.State }.Where(s => !string.IsNullOrWhiteSpace(s))), e.Entrants,
                e.Brackets.Select(b => new DigestBracket(b.GameId, b.GameName, b.TournamentId, b.UrlSlug, b.StartTimeUtc, b.Entrants)).ToArray(), e.EndTimeUtc)).ToArray(),
                preview.HasMore, GameOrder: filter.GameIds, SubscriptionVersion: feed.Version);
            if (digest.Events.Count > 0)
            {
                var take = DiscordTimelineTemplate.FittingEventCount(digest);
                announced = preview.Events.Take(take).Select(e => e.EditionId).Distinct().ToArray();
                var remaining = preview.Events.Skip(take).Select(e => e.EditionId).ToArray();
                // A changed MaxEvents must not discard the tail of an existing scheduled cursor.
                if (deferred.Length > 0 && preview.HasMore)
                    remaining = remaining.Concat(deferred.Except(preview.Events.Select(e => e.EditionId))).Distinct().ToArray();
                digest = digest with { Events = digest.Events.Take(take).ToArray(), HasMore = preview.HasMore || remaining.Length > 0,
                    DeferredEditionIds = remaining };
                retainWork = digest.HasMore;
                _ = DiscordEventFeedOutlet.Payload(digest);
                payload = JsonSerializer.Serialize(digest); status = "Pending"; outcome = "Queued";
            }
        }
        // An administrator can retire a selected catalogue entry. Pause this feed without blocking other owners.
        catch (ArgumentException) { status = "Failed"; outcome = "InvalidFiltersOrEventData"; Sengoku.Observability.OperationLog.Current?.Reject(outcome); }
        Sengoku.Observability.OperationLog.Data(("FeedId", feed.Id), ("Outcome", outcome));
        var deliveryId = Guid.NewGuid();
        await conn.ExecuteAsync(Cmd("SELECT event_feed_enqueue_v2(@deliveryId,@id,@scheduled,@status,CAST(@payload AS jsonb),@outcome,CAST(@insertedIds AS integer[]))",
            new { deliveryId, id = feed.Id, scheduled = feed.NextRunAtUtc, status, payload, outcome, insertedIds = Array.Empty<int>() }));
        if (status == "Pending")
        {
            var expires = (work.Length > 0 ? work.Min(w => w.CreatedAt) : DateTime.UtcNow).AddDays(7);
            var updated = await conn.ExecuteAsync(Cmd("UPDATE event_feed_deliveries SET expires_at=@expires WHERE id=@deliveryId", new { expires, deliveryId }));
            if (updated != 1) throw new InvalidOperationException("Delivery snapshot was not reserved.");
            if (work.Length > 0)
                await conn.ExecuteAsync(Cmd("""
                    INSERT INTO event_feed_edition_history(subscription_id,edition_id,delivery_id)
                    SELECT @id,edition,@deliveryId FROM unnest(@announced) edition
                    """, new { id = feed.Id, deliveryId, announced }));
        }
        // Retain the wake-up (including reconnect's edition 0) until every complete parent
        // fits. History excludes only announced editions, so the next page makes progress.
        if (processed.Length > 0 && !retainWork)
            await conn.ExecuteAsync(Cmd("DELETE FROM event_feed_edition_work WHERE subscription_id=@id AND edition_id=ANY(@processed)", new { id = feed.Id, processed }));
        await tx.CommitAsync(ct);
        Sengoku.Observability.OperationLog.Event("Database.Committed", ("Committed", true));
        return true;
    }, ct);

    private sealed record EditionWork(int EditionId, DateTime CreatedAt);

    private Task<DispatchRow?> ClaimNext(CancellationToken ct) => Database(async conn =>
    {
        while (true)
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            // The existing claim locks the subscription and handles expiry. Undo the entire claim
            // before deferring so attempts, sending_at and the encrypted snapshot stay intact.
            await tx.SaveAsync("before_claim", ct);
            var job = await conn.QueryFirstOrDefaultAsync<DispatchRow>(new CommandDefinition(
                "SELECT * FROM event_feed_claim_current()", transaction: tx, cancellationToken: ct));
            if (job != null)
            {
                var due = await conn.ExecuteScalarAsync<DateTime?>(new CommandDefinition("""
                    SELECT max(completed_at) + interval '5 hours' FROM event_feed_deliveries
                    WHERE subscription_id=@subscriptionId AND status='Sent'
                      AND completed_at > clock_timestamp() - interval '5 hours'
                    """, new { subscriptionId = job.FeedId }, tx, cancellationToken: ct));
                if (due.HasValue)
                {
                    await tx.RollbackAsync("before_claim", ct);
                    await conn.ExecuteAsync(new CommandDefinition("""
                        UPDATE event_feed_deliveries SET next_attempt_at=greatest(next_attempt_at,@due)
                        WHERE id=@id AND status='Pending'
                        """, new { job.Id, due }, tx, cancellationToken: ct));
                    await tx.CommitAsync(ct);
                    continue; // Another subscription can be ready in the same worker cycle.
                }
            }
            await tx.CommitAsync(ct);
            return job;
        }
    }, ct);

    public async Task<bool> DispatchNext(IEventFeedOutlet outlet, CancellationToken ct)
    {
        var job = await ClaimNext(ct);
        if (job == null) return false;
        Sengoku.Observability.OperationLog.Data(("MessageId", job.Id));
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
        Sengoku.Observability.OperationLog.Data(("Outcome", result.Status));
        if (result.Status == "Retry") Sengoku.Observability.OperationLog.Current?.Pending();
        else if (result.Status != "Sent") Sengoku.Observability.OperationLog.Current?.Reject("DeliveryNotConfirmed");

        // Finalize ambiguous cancellation even when shutdown cancelled the caller token.
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var sent = result.Status == "Sent" ? JsonSerializer.Deserialize<EventDigest>(job.Payload) : null;
        var remainder = sent?.DeferredEditionIds;
        await Database(async conn =>
        {
            await using var tx = await conn.BeginTransactionAsync(cleanup.Token);
            var completed = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT event_feed_complete(@id,@outcome,@messageId,@delay)",
                new { id = job.Id, outcome = result.Status, messageId = result.MessageId, delay = Math.Max(1, result.RetryAfterSeconds) }, tx, cancellationToken: cleanup.Token));
            // Completion clears the sensitive snapshot. Save the ID-only cursor atomically with
            // its acknowledgement so Daily/Interval overflow survives worker restarts and long cadences.
            if (completed && remainder is { Length: > 0 })
                await conn.ExecuteAsync(new CommandDefinition("UPDATE event_feed_deliveries SET payload=CAST(@cursor AS jsonb) WHERE id=@id AND status='Sent'",
                    new { id = job.Id, cursor = JsonSerializer.Serialize(new { DeferredEditionIds = remainder, sent!.SubscriptionVersion }) }, tx, cancellationToken: cleanup.Token));
            await tx.CommitAsync(cleanup.Token);
            return completed;
        }, cleanup.Token);
        return true;
    }

    public Task<int> RecoverInterrupted(CancellationToken ct) => Database(conn => conn.ExecuteScalarAsync<int>(
        new CommandDefinition("SELECT event_feed_recover()", cancellationToken: ct)), ct);
}
