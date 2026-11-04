using Dapper;
using SengokuDispatcher.Library.Models.Feeds;

namespace SengokuDispatcher.Library.Services.Feeds;

public sealed partial class EventFeedStore
{
    public Task<IReadOnlyList<MajorCalendarEdition>> MajorCalendar(CancellationToken ct) =>
        Database<IReadOnlyList<MajorCalendarEdition>>(async conn => (await conn.QueryAsync<MajorCalendarEdition>(
            new CommandDefinition("""
                SELECT id,version,active,edition_name AS Name,circuit,startgg_id AS StartggId,game_ids AS GameIds,
                    start_utc AT TIME ZONE 'UTC' AS StartsAt,end_utc AT TIME ZONE 'UTC' AS EndsAt,
                    registration_opens_utc AT TIME ZONE 'UTC' AS RegistrationOpensAt,
                    registration_closes_utc AT TIME ZONE 'UTC' AS RegistrationClosesAt,city,state,country,online
                FROM major_circuit_calendar ORDER BY start_utc,id
                """, cancellationToken: ct))).ToList(), ct);

    public Task<int> SaveMajor(int userId, int? id, MajorCalendarEdition input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 180 ||
            input.GameIds is not { Length: > 0 and <= 100 } || input.GameIds.Any(g => g <= 0) ||
            input.StartggId is <= 0 || input.StartsAt == default || input.EndsAt < input.StartsAt ||
            input.RegistrationClosesAt < input.RegistrationOpensAt ||
            (!input.Online && (string.IsNullOrWhiteSpace(input.Country) || string.IsNullOrWhiteSpace(input.State))))
            throw new ArgumentException("Provide a name, game IDs, ordered dates and an in-person location.");
        return Database(async conn =>
        {
            await using var tx = await conn.BeginTransactionAsync(ct);
            var values = new { userId, id, input.Version, name = input.Name.Trim(), input.Circuit, input.StartggId,
                games = input.GameIds.Distinct().ToArray(), starts = UtcClock(input.StartsAt), ends = UtcClock(input.EndsAt),
                opens = UtcClock(input.RegistrationOpensAt), closes = UtcClock(input.RegistrationClosesAt),
                city = input.City?.Trim(), state = input.State?.Trim().ToUpperInvariant(),
                country = input.Country?.Trim().ToUpperInvariant(), input.Online, input.Active };
            CommandDefinition Command(string sql) => new(sql, values, tx, cancellationToken: ct);
            if (!await conn.ExecuteScalarAsync<bool>(Command("SELECT event_feed_is_admin(@userId)")))
                throw new UnauthorizedAccessException();
            if (!await conn.ExecuteScalarAsync<bool>(Command("SELECT count(*)=cardinality(@games) FROM games WHERE id=ANY(@games)")))
                throw new ArgumentException("Unknown game ID.");
            var sql = id == null ? """
                INSERT INTO major_circuit_calendar(edition_name,circuit,startgg_id,game_ids,start_utc,end_utc,
                    registration_opens_utc,registration_closes_utc,city,state,country,online,active,updated_by)
                VALUES(@name,@circuit,@startggId,@games,@starts,@ends,@opens,@closes,@city,@state,@country,@online,@active,@userId)
                ON CONFLICT(startgg_id) DO NOTHING RETURNING id
                """ : """
                UPDATE major_circuit_calendar SET edition_name=@name,circuit=@circuit,startgg_id=@startggId,game_ids=@games,
                    start_utc=@starts,end_utc=@ends,registration_opens_utc=@opens,registration_closes_utc=@closes,
                    city=@city,state=@state,country=@country,online=@online,active=@active,updated_by=@userId,
                    refresh_after=now(),version=version+1
                WHERE id=@id AND version=@version AND (startgg_id IS NULL OR startgg_id=@startggId) RETURNING id
                """;
            int? saved;
            try { saved = await conn.ExecuteScalarAsync<int?>(Command(sql)); }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
            { throw new FeedConflictException("This start.gg edition is already linked."); }
            if (!saved.HasValue) throw new FeedConflictException("Calendar edition or link changed. Reload before retrying.");
            await tx.CommitAsync(ct);
            return saved.Value;
        }, ct);
    }

    private static DateTime? UtcClock(DateTimeOffset? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value.UtcDateTime, DateTimeKind.Unspecified) : null;
}
