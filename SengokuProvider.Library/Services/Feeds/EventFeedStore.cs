using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Npgsql;
using SengokuProvider.Library.Models.Feeds;
using System.Text.Json;

namespace SengokuProvider.Library.Services.Feeds;

public sealed partial class EventFeedStore(string connectionString, IDataProtectionProvider protection) : IEventFeedStore, IEventFeedDispatcher
{
    private readonly IDataProtector _protector = protection.CreateProtector("Sengoku.EventFeeds.Discord.v1");
    private const string Columns = "id, feed_id AS FeedId, user_id AS UserId, name, provider, destination_protected AS DestinationProtected, filters::text AS Filters, frequency_hours AS FrequencyHours, enabled, next_run_at AS NextRunAtUtc, version, schedule_mode AS ScheduleMode, daily_at_utc AS DailyAtUtc, discord_guild_id AS DiscordGuildId, discord_channel_id AS DiscordChannelId";
    private static EventFeedSubscriptionView View(FeedRow row) => new(row.Id, row.Name, row.Provider,
        JsonSerializer.Deserialize<EventFeedFilter>(row.Filters)!, new FeedSchedule 
        {
            Mode = row.ScheduleMode, 
            IntervalHours = row.ScheduleMode == "Interval" ? row.FrequencyHours : null,
            UtcTime = row.DailyAtUtc.HasValue ? TimeOnly.FromTimeSpan(row.DailyAtUtc.Value).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : null
        }, row.Enabled, row.ScheduleMode == "OnInsert" || row.DestinationProtected == null ? null : row.NextRunAtUtc,
        row.Version, row.DestinationProtected == null ? "AwaitingDiscord" : "Connected", row.DiscordGuildId, row.DiscordChannelId) { FeedId = row.FeedId };

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
            throw new FeedConflictException("The feed or subscription changed, or a delivery is in progress. Reload before retrying.");
        }
        catch (PostgresException ex) when (ex.SqlState == "42501") { throw new UnauthorizedAccessException("Administrator permission required."); }
        catch (PostgresException ex) when (ex.SqlState == "P4000") { throw new ArgumentException(ex.MessageText); }
        catch (NpgsqlException ex) { throw new ApplicationException("Event feed database operation failed.", ex); }
    }

    private const string DefinitionColumns = "id,user_id AS UserId,name,providers,filters::text AS Filters,frequency_hours AS FrequencyHours,schedule_mode AS ScheduleMode,daily_at_utc AS DailyAtUtc,enabled,version";
    private static EventFeedDefinition Definition(FeedRow r) => new(r.Id, r.UserId, r.Name, r.Providers,
        JsonSerializer.Deserialize<EventFeedFilter>(r.Filters)!, new FeedSchedule { Mode = r.ScheduleMode,
        IntervalHours = r.ScheduleMode == "Interval" ? r.FrequencyHours : null,
        UtcTime = r.DailyAtUtc.HasValue ? TimeOnly.FromTimeSpan(r.DailyAtUtc.Value).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : null }, r.Enabled, r.Version);
    public Task<IReadOnlyList<EventFeedDefinition>> List(int userId, CancellationToken ct) => Database<IReadOnlyList<EventFeedDefinition>>(async conn =>
        (await conn.QueryAsync<FeedRow>(new CommandDefinition($"SELECT {DefinitionColumns} FROM event_feeds WHERE enabled OR event_feed_is_admin(@userId) ORDER BY name,id", new { userId }, cancellationToken: ct))).Select(Definition).ToArray(), ct);
    public Task<EventFeedDefinition?> Get(int userId, Guid id, CancellationToken ct) => Database(async conn => {
        var row = await conn.QuerySingleOrDefaultAsync<FeedRow>(new CommandDefinition($"SELECT {DefinitionColumns} FROM event_feeds WHERE id=@id AND (enabled OR event_feed_is_admin(@userId))", new { id, userId }, cancellationToken: ct));
        return row == null ? null : Definition(row);
    }, ct);
    public Task<IReadOnlyList<EventFeedSubscriptionView>> Subscriptions(int userId, CancellationToken ct) => Database<IReadOnlyList<EventFeedSubscriptionView>>(async conn =>
        (await conn.QueryAsync<FeedRow>(new CommandDefinition($"SELECT {Columns} FROM event_feed_delivery_targets WHERE user_id=@userId ORDER BY name,id", new { userId }, cancellationToken: ct))).Select(View).ToArray(), ct);
    public Task<EventFeedSubscriptionView?> GetSubscription(int userId, Guid id, CancellationToken ct) => Database(async conn => {
        var row = await conn.QuerySingleOrDefaultAsync<FeedRow>(new CommandDefinition($"SELECT {Columns} FROM event_feed_delivery_targets WHERE id=@id AND user_id=@userId", new { id, userId }, cancellationToken: ct));
        return row == null ? null : View(row);
    }, ct);
    public Task<EventFeedSubscriptionView?> Subscribe(int userId, Guid feedId, string provider, CancellationToken ct)
    {
        if (provider != "Discord") throw new ArgumentException("Unsupported provider.");
        return Database(async conn => {
            await using var tx = await conn.BeginTransactionAsync(ct);
            var id = await conn.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition("SELECT id FROM event_feed_subscribe(@userId,@feedId,@provider)", new { userId, feedId, provider }, tx, cancellationToken: ct));
            if (id == null) return null;
            var row = await conn.QuerySingleAsync<FeedRow>(new CommandDefinition($"SELECT {Columns} FROM event_feed_delivery_targets WHERE id=@id", new { id }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return View(row);
        }, ct);
    }
    public Task<bool> Unsubscribe(int userId, Guid id, CancellationToken ct) => Database(conn => conn.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT event_feed_unsubscribe(@userId,@id)",new {userId,id},cancellationToken:ct)),ct);
    public Task<IReadOnlyList<FeedDelivery>> History(int userId, Guid id, CancellationToken ct) => Database<IReadOnlyList<FeedDelivery>>(async conn =>
        (await conn.QueryAsync<FeedDelivery>(new CommandDefinition("SELECT d.id,s.feed_id AS FeedId,d.subscription_id AS SubscriptionId,d.status,d.attempts,d.scheduled_for AS ScheduledFor,d.completed_at AS CompletedAt,d.message_id AS MessageId,d.outcome FROM event_feed_deliveries d JOIN event_feed_subscriptions s ON s.id=d.subscription_id WHERE s.id=@id AND s.user_id=@userId ORDER BY d.scheduled_for DESC LIMIT 50", new { id,userId }, cancellationToken:ct))).ToArray(),ct);

    public Task<EventFeedDefinition?> Save(int userId, Guid? id, SaveEventFeedRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 100 || request.Name.Any(char.IsControl)) throw new ArgumentException("Use a feed name of 1–100 characters.");

        if (request.Filter == null) throw new ArgumentException("Filters are required.");

        var filter = request.Filter.Normalize();
        if (request.Schedule == null) throw new ArgumentException("A schedule is required.");

        var daily = request.Schedule.Validate();
        if (request.Providers == null || request.Providers.Length != 1 || request.Providers[0] != "Discord") throw new ArgumentException("Select supported providers: Discord.");

        return Database(async conn =>
        {
            var row = await conn.QuerySingleOrDefaultAsync<FeedRow>(new CommandDefinition(
                $"SELECT {DefinitionColumns} FROM event_feed_save_v3(@userId,@id,@create,@name,CAST(@filters AS jsonb),@mode,CAST(@hours AS integer),CAST(@daily AS time),@enabled,@version,@providers)",
                new { userId, id = id ?? Guid.NewGuid(), create = id == null, name = request.Name.Trim(), mode = request.Schedule.Mode, daily,
                    filters = JsonSerializer.Serialize(filter), hours = request.Schedule.IntervalHours, enabled = request.Enabled, version = request.ExpectedVersion, providers = request.Providers }, cancellationToken: ct));
            return row == null ? null : Definition(row);
        }, ct);
    }
    public Task<bool> Delete(int userId, Guid id, CancellationToken ct) => Database(conn =>
        conn.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT event_feed_delete(@userId,@id)", new { userId, id }, cancellationToken: ct)), ct);
}
