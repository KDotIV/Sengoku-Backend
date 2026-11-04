using Dapper;
using Npgsql;
using SengokuProvider.Library.Models.Feeds;

namespace SengokuProvider.Library.Services.Events;

public partial class EventQueryService : IEventFeedQueryService
{
    public Task<EventFeedPreview> GetUpcomingFeedEvents(EventFeedFilter filter, DateTime nowUtc, CancellationToken ct = default, int[]? tournamentIds = null)
        => QueryEditions(filter, nowUtc, tournamentIds, null, null, ct);

    public Task<EventFeedPreview> GetEditionFeedEvents(EventFeedFilter filter, DateTime nowUtc, int[]? editionIds, Guid subscriptionId, CancellationToken ct)
        => QueryEditions(filter, nowUtc, null, editionIds, subscriptionId, ct);

    private async Task<EventFeedPreview> QueryEditions(EventFeedFilter filter, DateTime nowUtc, int[]? tournamentIds, int[]? editionIds, Guid? subscriptionId, CancellationToken ct)
    {
        filter = filter.Normalize();
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("An explicit UTC time is required.", nameof(nowUtc));
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            // Existing intake stores UTC clock values in timestamp-without-time-zone columns.
            const string sql = """
                SELECT * FROM event_feed_matching_editions(CAST(@definition AS jsonb),@start,
                    CASE WHEN CAST(@tournamentIds AS integer[]) IS NULL THEN CAST(@editionIds AS integer[])
                         ELSE ARRAY(SELECT event_link FROM tournament_links WHERE id=ANY(@tournamentIds)) END,
                    CAST(@subscriptionId AS uuid),@limit,CAST(@tournamentIds AS integer[]))
                """;
            var rows = (await conn.QueryAsync<UpcomingFeedEvent>(new CommandDefinition(sql, new {
                definition = System.Text.Json.JsonSerializer.Serialize(filter), tournamentIds, editionIds, subscriptionId,
                start = DateTime.SpecifyKind(nowUtc, DateTimeKind.Unspecified), limit = filter.MaxEvents + 1
            }, cancellationToken: ct))).ToList();

            foreach (var row in rows)
            {
                row.StartTimeUtc = DateTime.SpecifyKind(row.StartTimeUtc, DateTimeKind.Utc);
                if (row.EndTimeUtc.HasValue) row.EndTimeUtc = DateTime.SpecifyKind(row.EndTimeUtc.Value, DateTimeKind.Utc);
                if (row.LastUpdatedUtc.HasValue) row.LastUpdatedUtc = DateTime.SpecifyKind(row.LastUpdatedUtc.Value, DateTimeKind.Utc);
            }
            return new(rows.Take(filter.MaxEvents).ToArray(), rows.Count > filter.MaxEvents, nowUtc);
        }
        catch (PostgresException ex) when (ex.SqlState == "P4000") {
            Sengoku.Observability.OperationLog.Failure(ex); throw new ArgumentException(ex.MessageText); }
        catch (NpgsqlException ex) {
            Sengoku.Observability.OperationLog.Failure(ex); throw new ApplicationException("Unable to query upcoming feed events.", ex); }
    }
}


