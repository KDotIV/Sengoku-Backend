using Microsoft.Extensions.Configuration;
using SengokuProvider.Library.Models.Feeds;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SengokuProvider.Library.Services.Feeds;

public interface IDiscordFeedConnection
{
    bool Enabled { get; }
    Task<string> Begin(int userId, Guid feedId, string sessionToken, CancellationToken ct);
    Task<EventFeedView> Complete(int userId, string sessionToken, string? state, string? code, string? error, CancellationToken ct);
}

public sealed class DiscordFeedConnection(IConfiguration config, IHttpClientFactory clients, IDiscordFeedConnectionStore connections, IEventFeedStore feeds) : IDiscordFeedConnection
{
    public bool Enabled => !string.IsNullOrWhiteSpace(config["DiscordOAuth:ClientId"]) && !string.IsNullOrWhiteSpace(config["DiscordOAuth:ClientSecret"])
        && Uri.TryCreate(config["DiscordOAuth:RedirectUri"], UriKind.Absolute, out var uri) && uri.Scheme == "https"
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private void CheckEnabled() { if (!Enabled) throw new InvalidOperationException("Discord connection is not configured."); }

    public async Task<string> Begin(int userId, Guid feedId, string sessionToken, CancellationToken ct)
    {
        CheckEnabled();

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        if (!await connections.BeginAuthorization(userId, feedId, Hash(sessionToken), Hash(state), ct))
            throw new ArgumentException("Feed or session unavailable. Reload and try again.");

        return "https://discord.com/oauth2/authorize?response_type=code&scope=webhook.incoming&client_id="
            + Uri.EscapeDataString(config["DiscordOAuth:ClientId"]!) + "&redirect_uri=" + Uri.EscapeDataString(config["DiscordOAuth:RedirectUri"]!) + "&state=" + state;
    }

    public async Task<EventFeedView> Complete(int userId, string sessionToken, string? state, string? code, string? error, CancellationToken ct)
    {
        CheckEnabled();
        if (state is not { Length: 64 } || !state.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid Discord authorization state.");

        var pending = await connections.ConsumeAuthorization(userId, Hash(sessionToken), Hash(state), ct)
            ?? throw new ArgumentException("Discord authorization expired or was already used. Reconnect Discord.");

        if (!string.IsNullOrEmpty(error)) throw new ArgumentException("Discord connection was declined.");
        if (string.IsNullOrWhiteSpace(code) || code.Length > 4096) throw new ArgumentException("Missing Discord authorization code.");

        using var client = clients.CreateClient("DiscordFeedOAuth");

        using var form = new FormUrlEncodedContent(new Dictionary<string,string> {
            ["client_id"] = config["DiscordOAuth:ClientId"]!, ["client_secret"] = config["DiscordOAuth:ClientSecret"]!,
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = config["DiscordOAuth:RedirectUri"]!
        });

        using var response = await client.PostAsync("https://discord.com/api/v10/oauth2/token", form, ct);

        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Discord rejected authorization. Reconnect Discord.");

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        if (!json.RootElement.TryGetProperty("webhook", out var webhook) || webhook.ValueKind != JsonValueKind.Object)
            throw new HttpRequestException("Discord did not return a channel webhook.");

        static string Required(JsonElement obj, string property) => obj.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!
            : throw new HttpRequestException("Discord returned an incomplete webhook.");

        var id = Required(webhook, "id");
        var token = Required(webhook, "token");
        var guild = Required(webhook, "guild_id");
        var channel = Required(webhook, "channel_id");
        var destination = DiscordEventFeedOutlet.ValidateWebhook($"https://discord.com/api/webhooks/{id}/{token}");

        try
        {
            if (!await connections.ConnectDiscord(userId, pending.FeedId, pending.Version, destination, guild, channel, ct))
                throw new ArgumentException("The feed was removed. Create a feed before connecting Discord.");
        }
        catch (FeedConflictException)
        {
            // Authorization created this webhook, but an intervening edit prevented attaching it.
            await RemoveUnattached(client, destination);
            throw;
        }
        catch (ArgumentException)
        {
            await RemoveUnattached(client, destination);
            throw;
        }
        // Do not return provider tokens or webhook secrets to the browser.
        return await feeds.Get(userId, pending.FeedId, ct) ?? throw new ArgumentException("The feed was removed.");
    }
    private static async Task RemoveUnattached(HttpClient client, string destination)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { using var response = await client.DeleteAsync(destination, timeout.Token); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { }
    }
}
