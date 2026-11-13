using System.Text.Json;
using SengokuDispatcher.Library.Services.Feeds;

// Offline renderer: input is the data object from a public start.gg tournament query.
// It never reads credentials, calls a webhook, or updates a database.
internal static class TimelinePreview
{
    internal static void Write(string sourceFile, string outputFile)
    {
        using var source = JsonDocument.Parse(File.ReadAllText(sourceFile));
        var games = new[] { 33945, 48548 };
        var events = source.RootElement.EnumerateObject().Select(p => p.Value).Select(e => new DigestEvent(
            e.GetProperty("name").GetString()!, e.GetProperty("slug").GetString()!,
            DateTimeOffset.FromUnixTimeSeconds(e.GetProperty("startAt").GetInt64()).UtcDateTime,
            e.GetProperty("city").GetString() + ", " + e.GetProperty("addrState").GetString(), null,
            e.GetProperty("events").EnumerateArray().Where(b => games.Contains(b.GetProperty("videogame").GetProperty("id").GetInt32()))
                .Select(b => new DigestBracket(b.GetProperty("videogame").GetProperty("id").GetInt32(), b.GetProperty("videogame").GetProperty("name").GetString()!,
                    b.GetProperty("id").GetInt32(), b.GetProperty("slug").GetString()!,
                    b.GetProperty("startAt").ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(b.GetProperty("startAt").GetInt64()).UtcDateTime : null,
                    b.GetProperty("numEntrants").ValueKind == JsonValueKind.Number ? b.GetProperty("numEntrants").GetInt32() : null)).ToArray()))
            .Where(e => e.Brackets!.Count > 0).OrderBy(e => e.StartUtc).ToArray();
        var digest = new EventDigest("Southeast Anime Feed", events, false, GameOrder: games);
        var count = DiscordTimelineTemplate.FittingEventCount(digest);
        digest = digest with { Events = events.Take(count).ToArray(), HasMore = count < events.Length };
        File.WriteAllText(outputFile, JsonSerializer.Serialize(DiscordEventFeedOutlet.Payload(digest, "https://preview.invalid"), new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Rendered {count}/{events.Length} complete tournaments into one local payload; no message sent.");
    }
}
