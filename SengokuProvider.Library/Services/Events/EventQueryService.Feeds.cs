using Dapper;
using Npgsql;
using SengokuProvider.Library.Models.Feeds;

namespace SengokuProvider.Library.Services.Events;

public partial class EventQueryService : IEventFeedQueryService
{
    public async Task<EventFeedPreview> GetUpcomingFeedEvents(EventFeedFilter filter, DateTime nowUtc, CancellationToken ct = default, int[]? tournamentIds = null)
    {
        filter = filter.Normalize();
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("An explicit UTC time is required.", nameof(nowUtc));
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            // Existing intake stores UTC clock values in timestamp-without-time-zone columns.
            var rows = (await conn.QueryAsync<UpcomingFeedEvent>(new CommandDefinition("SELECT * FROM event_feed_upcoming_v2(@games,@start,@finish,@minimum,@openOnly,@attendance,@regions,@states,CAST(@city AS text),@limit,CAST(@tournamentIds AS integer[]))", new {
                    games = filter.GameIds, regions = filter.RegionIds, tournamentIds, 
                    start = DateTime.SpecifyKind(nowUtc, DateTimeKind.Unspecified),
                    finish = DateTime.SpecifyKind(nowUtc.AddDays(filter.LookAheadDays), DateTimeKind.Unspecified),
                    minimum = filter.MinimumEntrants, openOnly = filter.RegistrationOpenOnly,
                    attendance = filter.Attendance, states = filter.States, city = filter.City, limit = filter.MaxEvents + 1
                }, cancellationToken: ct))).ToList();

            foreach (var row in rows)
            {
                row.StartTimeUtc = DateTime.SpecifyKind(row.StartTimeUtc, DateTimeKind.Utc);
                if (row.EndTimeUtc.HasValue) row.EndTimeUtc = DateTime.SpecifyKind(row.EndTimeUtc.Value, DateTimeKind.Utc);
                if (row.LastUpdatedUtc.HasValue) row.LastUpdatedUtc = DateTime.SpecifyKind(row.LastUpdatedUtc.Value, DateTimeKind.Utc);
            }
            return new(rows.Take(filter.MaxEvents).ToArray(), rows.Count > filter.MaxEvents, nowUtc);
        }
        catch (PostgresException ex) when (ex.SqlState == "P4000") { throw new ArgumentException(ex.MessageText); }
        catch (NpgsqlException ex) { throw new ApplicationException("Unable to query upcoming feed events.", ex); }
    }
}


