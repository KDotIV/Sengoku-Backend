using Dapper;
using Microsoft.AspNetCore.DataProtection;
using SengokuProvider.Library.Models.Feeds;

namespace SengokuProvider.Library.Services.Feeds;

public sealed record DiscordAuthorizationState(Guid FeedId, int Version);
public interface IDiscordFeedConnectionStore
{
    Task<bool> BeginAuthorization(int userId, Guid feedId, string sessionHash, string stateHash, CancellationToken ct);
    Task<DiscordAuthorizationState?> ConsumeAuthorization(int userId, string sessionHash, string stateHash, CancellationToken ct);
    Task<bool> ConnectDiscord(int userId, Guid feedId, int version, string webhook, string guildId, string channelId, CancellationToken ct);
}

public sealed partial class EventFeedStore : IDiscordFeedConnectionStore
{
    public Task<bool> BeginAuthorization(int userId, Guid feedId, string sessionHash, string stateHash, CancellationToken ct) => Database(conn =>
        conn.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT event_feed_oauth_begin(@userId,@feedId,@sessionHash,@stateHash)",
            new { userId, feedId, sessionHash, stateHash }, cancellationToken: ct)), ct);

    public Task<DiscordAuthorizationState?> ConsumeAuthorization(int userId, string sessionHash, string stateHash, CancellationToken ct) => Database(conn =>
        conn.QuerySingleOrDefaultAsync<DiscordAuthorizationState>(new CommandDefinition("SELECT * FROM event_feed_oauth_consume(@userId,@sessionHash,@stateHash)",
            new { userId, sessionHash, stateHash }, cancellationToken: ct)), ct);

    public Task<bool> ConnectDiscord(int userId, Guid feedId, int version, string webhook, string guildId, string channelId, CancellationToken ct)
    {
        var secret = _protector.Protect(DiscordEventFeedOutlet.ValidateWebhook(webhook));
        return Database(conn => conn.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT event_feed_connect(@userId,@feedId,@version,@secret,@guildId,@channelId)",
            new { userId, feedId, version, secret, guildId, channelId }, cancellationToken: ct)), ct);
    }
}
