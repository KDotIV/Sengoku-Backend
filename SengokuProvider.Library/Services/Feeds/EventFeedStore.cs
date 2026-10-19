using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using SengokuProvider.Library.Models.Feeds;
using System.Text.Json;

namespace SengokuProvider.Library.Services.Feeds;

public sealed partial class EventFeedStore(string connectionString, IDataProtectionProvider protection) : IEventFeedStore, IEventFeedDispatcher
{
    private readonly IDataProtector _protector = protection.CreateProtector("Sengoku.EventFeeds.Discord.v1");
    private const string Columns = "id, user_id AS UserId, name, provider, destination_protected AS DestinationProtected, filters::text AS Filters, frequency_hours AS FrequencyHours, enabled, next_run_at AS NextRunAtUtc, version, schedule_mode AS ScheduleMode, daily_at_utc AS DailyAtUtc, discord_guild_id AS DiscordGuildId, discord_channel_id AS DiscordChannelId";
    private static EventFeedView View(FeedRow row) => new(row.Id, row.Name, row.Provider,
        JsonSerializer.Deserialize<EventFeedFilter>(row.Filters)!, new FeedSchedule 
        {
            Mode = row.ScheduleMode, 
            IntervalHours = row.ScheduleMode == "Interval" ? row.FrequencyHours : null,
            UtcTime = row.DailyAtUtc.HasValue ? TimeOnly.FromTimeSpan(row.DailyAtUtc.Value).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : null
        }, row.Enabled, row.ScheduleMode == "OnInsert" || row.DestinationProtected == null ? null : row.NextRunAtUtc,
        row.Version, row.DestinationProtected == null ? "AwaitingDiscord" : "Connected", row.DiscordGuildId, row.DiscordChannelId);

    public Task<FeedCatalogue> Options(CancellationToken ct) => Database(async conn =>
    {
        var json = await conn.ExecuteScalarAsync<string>(new CommandDefinition("SELECT event_feed_catalogue()::text", cancellationToken: ct))
            ?? throw new InvalidOperationException("Feed catalogue is unavailable.");

        return JsonSerializer.Deserialize<FeedCatalogue>(json) ?? throw new InvalidOperationException("Feed catalogue is invalid.");
    }, ct);
    private async Task<T> Database<T>(Func<NpgsqlConnection, Task<T>> action, CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);
            return await action(conn);
        }
        // Only our explicit business-conflict SQLSTATE is translated to an API conflict.
        catch (PostgresException ex) when (ex.SqlState == "P4090")
        { 
            throw new FeedConflictException("The feed changed, a delivery is in progress, or the account feed limit was reached. Reload before retrying."); 
        }
        catch (PostgresException ex) when (ex.SqlState == "P4000") { throw new ArgumentException(ex.MessageText); }
        catch (NpgsqlException ex) { throw new ApplicationException("Event feed database operation failed.", ex); }
    }

    public Task<IReadOnlyList<EventFeedView>> List(int userId, CancellationToken ct) => Database<IReadOnlyList<EventFeedView>>(async conn =>
        (await conn.QueryAsync<FeedRow>(new CommandDefinition(
            $"SELECT {Columns} FROM event_feeds WHERE user_id=@userId ORDER BY created_at DESC LIMIT 50", new { userId }, cancellationToken: ct))).Select(View).ToArray(), ct);

    public Task<EventFeedView?> Get(int userId, Guid id, CancellationToken ct) => Database(async conn =>
    {
        var row = await conn.QuerySingleOrDefaultAsync<FeedRow>(new CommandDefinition(
            $"SELECT {Columns} FROM event_feeds WHERE id=@id AND user_id=@userId", new { id, userId }, cancellationToken: ct));
        return row == null ? null : View(row);
    }, ct);

    public Task<IReadOnlyList<FeedDelivery>> History(int userId, Guid id, CancellationToken ct) => Database<IReadOnlyList<FeedDelivery>>(async conn =>
        (await conn.QueryAsync<FeedDelivery>(new CommandDefinition(
            "SELECT * FROM event_feed_history(@userId,@id)", new { id, userId }, cancellationToken: ct))).ToArray(), ct);

    public Task<EventFeedView?> Save(int userId, Guid? id, SaveEventFeedRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100 || request.Name.Any(char.IsControl)) throw new ArgumentException("Use a feed name of 1–100 characters.");

        if (request.Filter == null) throw new ArgumentException("Filters are required.");

        var filter = request.Filter.Normalize();
        if (request.Schedule == null) throw new ArgumentException("A schedule is required.");

        var daily = request.Schedule.Validate();

        return Database(async conn =>
        {
            var row = await conn.QuerySingleOrDefaultAsync<FeedRow>(new CommandDefinition(
                $"SELECT {Columns} FROM event_feed_save_v2(@userId,@id,@create,@name,CAST(@filters AS jsonb),@mode,CAST(@hours AS integer),CAST(@daily AS time),@enabled,@version)",
                new { userId, id = id ?? Guid.NewGuid(), create = id == null, name = request.Name.Trim(), mode = request.Schedule.Mode, daily,
                    filters = JsonSerializer.Serialize(filter), hours = request.Schedule.IntervalHours, enabled = request.Enabled, version = request.ExpectedVersion }, cancellationToken: ct));
            return row == null ? null : View(row);
        }, ct);
    }
    public Task<bool> Delete(int userId, Guid id, CancellationToken ct) => Database(conn =>
        conn.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT event_feed_delete(@userId,@id)", new { userId, id }, cancellationToken: ct)), ct);
}