using System.Net;
using System.Text.Json;
using SengokuProvider.Library.Models.Feeds;

namespace SengokuProvider.Library.Services.Feeds;

public sealed class ConnectDiscordWebhookRequest
{
    public Guid? FeedId { get; init; }
    public string? FeedName { get; init; }
    public string WebhookUrl { get; init; } = "";
    public int? ExpectedVersion { get; init; }
}

public sealed class DiscordWebhookConnection(IHttpClientFactory clients, IEventFeedStore feeds, IDiscordFeedConnectionStore connections)
{
    public async Task<EventFeedView?> Connect(int userId, ConnectDiscordWebhookRequest request, CancellationToken ct)
    {
        var name = request.FeedName?.Trim();
        if (request.FeedId.HasValue == !string.IsNullOrEmpty(name) || request.FeedId == Guid.Empty
            || name?.Length > 100 || request.ExpectedVersion is < 1)
            throw new ArgumentException("Provide exactly one of feedId or feedName, and a positive expectedVersion if supplied.");

        EventFeedView? feed;
        if (request.FeedId is Guid id) feed = await feeds.Get(userId, id, ct);
        else
        {
            var matches = (await feeds.List(userId, ct)).Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1) throw new FeedConflictException("Multiple feeds have that name. Use feedId instead.");
            feed = matches.SingleOrDefault();
        }
        if (feed == null) return null;
        if (request.ExpectedVersion is int version && version != feed.Version)
            throw new FeedConflictException("The feed changed. Reload before reconnecting.");

        var destination = DiscordEventFeedOutlet.ValidateWebhook(request.WebhookUrl);
        using var client = clients.CreateClient("DiscordFeedOAuth"); // Redirects and URL logging are disabled.
        using var response = await client.GetAsync(destination, ct);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new ArgumentException("The Discord webhook is invalid or no longer available.");
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Unable to verify the Discord webhook. Try again later.");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;

        static string Snowflake(JsonElement obj, string property)
        {
            if (obj.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
                && value.GetString() is string s && s.Length is >= 17 and <= 20 && s.All(char.IsAsciiDigit)) return s;
            throw new ArgumentException("Discord did not return a valid server/channel webhook.");
        }

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.Number || !type.TryGetInt32(out var kind) || kind != 1)
            throw new ArgumentException("Use an incoming Discord channel webhook.");

        if (Snowflake(root, "id") != new Uri(destination).Segments[^2].TrimEnd('/'))
            throw new ArgumentException("Discord returned a different webhook.");

        var guild = Snowflake(root, "guild_id");
        var channel = Snowflake(root, "channel_id");
        // The stored version also protects callers who omit expectedVersion against concurrent edits.
        // An existing user-owned webhook must never be deleted on an attachment conflict.

        if (!await connections.ConnectDiscord(userId, feed.Id, feed.Version, destination, guild, channel, ct)) return null;

        return await feeds.Get(userId, feed.Id, ct);
    }
}
