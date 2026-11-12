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
            await conn.OpenAsync(ct);
            await using var snapshot = await conn.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
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
            }, snapshot, cancellationToken: ct))).ToList();

            var selected = rows.Take(filter.MaxEvents).ToArray();
            // Expand after applying the parent-edition limit. A multi-game tournament must not
            // consume several slots, and a later intake of one bracket must not hide its siblings.
            const string bracketsSql = """
                WITH matching AS (
                    SELECT e.edition_id AS "EditionId", l.id AS "TournamentId", l.game_id AS "GameId",
                           g.game_name AS "GameName", l.url_slug AS "UrlSlug", l.start_time AS "StartTimeUtc",
                           l.entrants_num AS "Entrants"
                    FROM event_feed_editions e JOIN tournament_links l ON l.event_link=e.link_id
                    JOIN games g ON g.id=l.game_id
                    WHERE e.edition_id=ANY(@selectedIds) AND l.game_id=ANY(@games)
                        AND (@minimum=0 OR l.entrants_num>=@minimum) AND NULLIF(l.url_slug,'') IS NOT NULL
                )
                SELECT * FROM matching
                UNION ALL
                SELECT e.edition_id,0,g.id,g.game_name,'',NULL::timestamptz,NULL::integer
                FROM event_feed_editions e CROSS JOIN LATERAL unnest(e.calendar_games) gid
                JOIN games g ON g.id=gid
                WHERE e.edition_id=ANY(@selectedIds) AND g.id=ANY(@games) AND @minimum=0
                    AND NOT EXISTS(SELECT FROM matching m WHERE m."EditionId"=e.edition_id AND m."GameId"=g.id)
                """;
            var brackets = selected.Length == 0 ? [] : (await conn.QueryAsync<FeedBracket>(new CommandDefinition(bracketsSql,
                new { selectedIds = selected.Select(e => e.EditionId).ToArray(), games = filter.GameIds, minimum = filter.MinimumEntrants },
                snapshot, cancellationToken: ct))).ToArray();
            foreach (var row in selected)
            {
                row.Brackets = brackets.Where(b => b.EditionId == row.EditionId)
                    .OrderBy(b => Array.IndexOf(filter.GameIds, b.GameId)).ThenBy(b => b.StartTimeUtc ?? row.StartTimeUtc)
                    .ThenBy(b => b.TournamentId).ToArray();
                row.StartTimeUtc = DateTime.SpecifyKind(row.StartTimeUtc, DateTimeKind.Utc);
                if (row.EndTimeUtc.HasValue) row.EndTimeUtc = DateTime.SpecifyKind(row.EndTimeUtc.Value, DateTimeKind.Utc);
                if (row.LastUpdatedUtc.HasValue) row.LastUpdatedUtc = DateTime.SpecifyKind(row.LastUpdatedUtc.Value, DateTimeKind.Utc);
            }
            await snapshot.CommitAsync(ct);
            return new(selected, rows.Count > filter.MaxEvents, nowUtc);
        }
        catch (PostgresException ex) when (ex.SqlState == "P4000") {
            Sengoku.Observability.OperationLog.Failure(ex); throw new ArgumentException(ex.MessageText); }
        catch (NpgsqlException ex) {
            Sengoku.Observability.OperationLog.Failure(ex); throw new ApplicationException("Unable to query upcoming feed events.", ex); }
    }
}


