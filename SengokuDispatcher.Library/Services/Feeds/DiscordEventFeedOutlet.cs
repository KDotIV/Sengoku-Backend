using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace SengokuDispatcher.Library.Services.Feeds;

public sealed record DigestBracket(int GameId, string GameName, int BracketId, string SignupSlug, DateTime? StartUtc, int? Entrants);
public sealed record DigestEvent(string Name, string SignupSlug, DateTime StartUtc, string Location, int? Entrants,
    IReadOnlyList<DigestBracket>? Brackets = null, DateTime? EndUtc = null);
public sealed record EventDigest(string Name, IReadOnlyList<DigestEvent> Events, bool HasMore,
    int[]? DeferredEditionIds = null, int[]? GameOrder = null, int SubscriptionVersion = 0);
public sealed class DiscordFeedPresentationOptions
{
    public string? AssetsBaseUrl { get; set; }
}
public sealed record DeliveryResult(string Status, string? MessageId = null, int RetryAfterSeconds = 0);
public interface IEventFeedOutlet
{
    string Provider { get; }
    Task<DeliveryResult> Send(string destination, EventDigest digest, CancellationToken ct);
}

public sealed class DiscordEventFeedOutlet(HttpClient client, IOptions<DiscordFeedPresentationOptions>? presentation = null) : IEventFeedOutlet
{
    public string Provider => "Discord";
    public static string ValidateWebhook(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 512 || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || uri.Host != "discord.com" || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || !Regex.IsMatch(uri.AbsolutePath, @"\A/api/(?:v10/)?webhooks/[0-9]{17,20}/[A-Za-z0-9_-]{20,200}\z"))
            throw new ArgumentException("Use an HTTPS discord.com webhook URL without query parameters.");

        return uri.AbsoluteUri;
    }
    public static string SignupUrl(string slug)
    {
        var path = slug?.Trim('/') ?? "";
        if (!Regex.IsMatch(path, @"\Atournament/[a-zA-Z0-9_-]+(?:/event/[a-zA-Z0-9_-]+)?\z"))
            throw new ArgumentException("Invalid start.gg tournament slug.");

        return "https://start.gg/" + path;
    }
    public static object Payload(EventDigest digest, string? assetsBaseUrl = null) => DiscordTimelineTemplate.Build(digest, assetsBaseUrl);
    public async Task<DeliveryResult> Send(string destination, EventDigest digest, CancellationToken ct)
    {
        var url = ValidateWebhook(destination);
        var payload = Payload(digest, presentation?.Value.AssetsBaseUrl);
        try
        {
            using var response = await client.PostAsJsonAsync(url + "?wait=true&with_components=true", payload, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var seconds = response.Headers.RetryAfter?.Delta?.TotalSeconds
                    ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? 60;
                try
                {
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    if (json.RootElement.TryGetProperty("retry_after", out var value) && value.TryGetDouble(out var retry) && double.IsFinite(retry)) seconds = Math.Max(seconds, retry);
                }
                catch (JsonException) { }
                if (!double.IsFinite(seconds) || seconds > int.MaxValue) return new("Rejected");
                return new("Retry", RetryAfterSeconds: (int)Math.Max(Math.Ceiling(seconds), 1));
            }
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new("InvalidDestination");

            // A server error or a dropped response may follow an accepted message. Do not automatically duplicate it.
            if ((int)response.StatusCode >= 500) return new("Unknown");
            if (!response.IsSuccessStatusCode) return new("Rejected");

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return body.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? new("Sent", id.GetString()) : new("Unknown");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            Sengoku.Observability.OperationLog.Failure(ex);
            // Do not leak webhook tokens through transport exception messages.
            return new("Unknown");
        }
    }
}

