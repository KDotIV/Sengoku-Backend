using System.Text.Json;
using SengokuDispatcher.Library.Services.Feeds;

internal static class TimelineChecks
{
    internal static void Run()
    {
        var day = new DateTime(2026, 11, 13, 18, 0, 0, DateTimeKind.Utc);
        DigestBracket Bracket(int game, int id, int hour) => new(game, game == 33945 ? "Guilty Gear Strive" : "Granblue Fantasy Versus Rising",
            id, "tournament/local/event/bracket-" + id, day.AddHours(hour), id);
        var first = new DigestEvent("First **tournament** @everyone", "tournament/first", day, "Atlanta, GA", 999,
            [Bracket(33945, 1, 4), Bracket(48548, 2, 2), Bracket(33945, 3, 5)]);
        var second = new DigestEvent("Second tournament", "tournament/second", day.AddHours(1), "Jacksonville, FL", 999,
            [Bracket(33945, 4, 3)]);
        var digest = new EventDigest("Southeast", [first, second], false, GameOrder: [33945, 48548]);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(DiscordEventFeedOutlet.Payload(digest, "https://feeds.example.test")));
        var sections = payload.RootElement.GetProperty("components")[0].GetProperty("components");
        var strive = sections[2].GetProperty("content").GetString()!;
        var granblue = sections[4].GetProperty("content").GetString()!;
        Check(strive.IndexOf("Second tournament", StringComparison.Ordinal) < strive.IndexOf("First", StringComparison.Ordinal), "order uses bracket time, not parent time");
        Check(strive.Contains("Bracket 1") && strive.Contains("Bracket 3") && !strive.Contains("Bracket 2"), "separate same-game brackets retained");
        Check(granblue.Contains("First") && !granblue.Contains("Second"), "only selected game matches in each section");
        Check(!strive.Contains("Also hosts") && !granblue.Contains("Also hosts") && strive.Contains("current entrants: 4") && !strive.Contains("current entrants: 999"),
            "bracket-level current entrant counts without cross-reference clutter");
        Check(!strive.Contains("@everyone") && strive.Contains("\\*\\*tournament\\*\\*"), "untrusted markup and mentions neutralized");
        Check(sections[1].GetProperty("type").GetInt32() == 12 && sections[1].GetProperty("items")[0].GetProperty("media").GetProperty("url").GetString()!.EndsWith("/33945.jpg"),
            "full-width game artwork precedes each section's text");
        Check(sections[3].GetProperty("type").GetInt32() == 12 && sections[3].GetProperty("items")[0].GetProperty("media").GetProperty("url").GetString()!.EndsWith("/48548.jpg"),
            "second game has its own full-width header");
        var unknown = first with { Brackets = [Bracket(33945, 1, 4) with { StartUtc = null }] };
        using var missingTime = JsonDocument.Parse(JsonSerializer.Serialize(DiscordEventFeedOutlet.Payload(digest with { Events = [unknown] })));
        var missingText = string.Join('\n', Texts(missingTime.RootElement));
        Check(!missingText.Contains("Time TBA") && !missingText.Contains("<t:") && missingText.Contains("↳ current entrants: 1"),
            "missing times stay blank without a parent-time substitute or dangling separator");
        var stale = first with { Brackets = [Bracket(33945, 1, -24)] };
        using var staleTime = JsonDocument.Parse(JsonSerializer.Serialize(DiscordEventFeedOutlet.Payload(digest with { Events = [stale] })));
        var staleText = string.Join('\n', Texts(staleTime.RootElement));
        Check(!staleText.Contains("Time needs confirmation") && !staleText.Contains("<t:") && staleText.Contains("current entrants: 1"),
            "inconsistent times stay blank rather than displaying a warning or invented schedule");
        using var legacy = JsonDocument.Parse(JsonSerializer.Serialize(DiscordEventFeedOutlet.Payload(
            JsonSerializer.Deserialize<EventDigest>("""{"Name":"Old queue","Events":[{"Name":"Legacy","SignupSlug":"tournament/legacy","StartUtc":"2026-11-13T18:00:00Z","Location":"Online","Entrants":2}],"HasMore":false}""")!)));
        Check(Texts(legacy.RootElement).Any(t => t.Contains("Tournament schedule")), "old queued snapshots remain deliverable");
        var many = digest with { Events = Enumerable.Range(0, 10).Select(i => first with { Name = new string('x', 140) + i }).ToArray() };
        var take = DiscordTimelineTemplate.FittingEventCount(many);
        Check(take is > 0 and < 10, "oversized timelines select whole parent tournaments");
        using var fitted = JsonDocument.Parse(JsonSerializer.Serialize(DiscordEventFeedOutlet.Payload(many with { Events = many.Events.Take(take).ToArray(), HasMore = true })));
        Check(Texts(fitted.RootElement).Sum(t => t.Length) <= DiscordTimelineTemplate.TextLimit, "aggregate text budget enforced");
        var catalog = new[] {33945,1386,43868,49783,17,4985,48548,610,36963,5582,48599,287,1,102,103,104,105,106,107,108};
        var crowded = first with { Brackets = catalog.Select((id, i) => Bracket(id, i + 1, i)).ToArray() };
        using var allGames = JsonDocument.Parse(JsonSerializer.Serialize(DiscordEventFeedOutlet.Payload(digest with { Events = [crowded] }, "https://feeds.example.test")));
        Check(CountComponents(allGames.RootElement) <= 40 && Texts(allGames.RootElement).Sum(t => t.Length) <= 4000, "20-game compact fallback fits one message");
        Check(Texts(allGames.RootElement).Count(t => t.StartsWith("### ")) == 20, "no selected game silently omitted");
        Check(GameHeaderCatalog.ImageUrl(999999, "https://feeds.example.test") == null, "unknown games get text-only heading");
        Console.WriteLine("PASS game timelines, bracket ordering/times/counts, multi-game schedules, safe Markdown, artwork, old snapshots and Discord limits");
    }
    internal static IEnumerable<string> Texts(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("type", out var type) && type.GetInt32() == 10) yield return root.GetProperty("content").GetString()!;
            foreach (var p in root.EnumerateObject()) foreach (var text in Texts(p.Value)) yield return text;
        }
        else if (root.ValueKind == JsonValueKind.Array) foreach (var child in root.EnumerateArray()) foreach (var text in Texts(child)) yield return text;
    }
    private static int CountComponents(JsonElement root) => root.ValueKind switch {
        JsonValueKind.Object => (root.TryGetProperty("type", out _) ? 1 : 0) + root.EnumerateObject().Sum(p => CountComponents(p.Value)),
        JsonValueKind.Array => root.EnumerateArray().Sum(CountComponents), _ => 0 };
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
}
