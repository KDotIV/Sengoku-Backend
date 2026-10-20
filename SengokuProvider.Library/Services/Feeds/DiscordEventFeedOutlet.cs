using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SengokuProvider.Library.Services.Feeds;

public sealed record DigestEvent(string Name, string SignupSlug, DateTime StartUtc, string Location, int? Entrants);
public sealed record EventDigest(string Name, IReadOnlyList<DigestEvent> Events, bool HasMore);
public sealed record DeliveryResult(string Status, string? MessageId = null, int RetryAfterSeconds = 0);
public interface IEventFeedOutlet
{
    string Provider { get; }
    Task<DeliveryResult> Send(string destination, EventDigest digest, CancellationToken ct);
}

public sealed class DiscordEventFeedOutlet(HttpClient client) : IEventFeedOutlet
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
    private static string Text(string value, int max) => new(value.Where(c => !char.IsControl(c)).Take(max).ToArray());
    public static object Payload(EventDigest digest)
    {
        if (digest.Events.Count is < 1 or > 10) throw new ArgumentException("A digest must contain 1–10 events.");
        var embeds = digest.Events.Select(e => new {
            title = Text(e.Name, 180), url = SignupUrl(e.SignupSlug),
            description = $"{Text(e.Location, 100)}" +
            $"\n<t:{new DateTimeOffset(DateTime.SpecifyKind(e.StartUtc, DateTimeKind.Utc)).ToUnixTimeSeconds()}:F>" +
            $"\nEntrants: {(e.Entrants?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Unknown")}\n[View / register on start.gg]({SignupUrl(e.SignupSlug)})"
        }).ToArray();

        if (embeds.Any(e => e.url.Length > 2048 || e.description.Length > 4096) || embeds.Sum(e => e.title.Length + e.description.Length) > 6000)
            throw new ArgumentException("Event data exceeds Discord's embed limits.");

        return new {
            content = Text(digest.Name, 100) + (digest.HasMore ? " — first matches; more events match your filters." : " — upcoming tournaments"),
            allowed_mentions = new { parse = Array.Empty<string>() },
            embeds
        };
    }
    public async Task<DeliveryResult> Send(string destination, EventDigest digest, CancellationToken ct)
    {
        var url = ValidateWebhook(destination);
        var payload = Payload(digest);
        try
        {
            using var response = await client.PostAsJsonAsync(url + "?wait=true", payload, ct);
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
            // Do not leak webhook tokens through transport exception messages.
            return new("Unknown");
        }
    }
}

