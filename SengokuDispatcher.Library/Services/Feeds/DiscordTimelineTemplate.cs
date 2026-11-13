using System.Globalization;
using System.Text;

namespace SengokuDispatcher.Library.Services.Feeds;

/// <summary>One Discord Components V2 message. Never split a tournament across deliveries.</summary>
public static class DiscordTimelineTemplate
{
    public const int TextLimit = 4000;
    public const int ComponentLimit = 40;

    public static int FittingEventCount(EventDigest digest)
    {
        for (var count = digest.Events.Count; count > 0; count--)
        {
            try
            {
                var more = digest.HasMore || count < digest.Events.Count;
                Build(digest with { Events = digest.Events.Take(count).ToArray(), HasMore = more,
                    DeferredEditionIds = more ? [0] : null }); // Reserve the longer overflow footer before recording actual IDs.
                return count;
            }
            catch (TimelineCapacityException) when (count > 1) { }
        }
        return 0;
    }

    public static object Build(EventDigest digest, string? assetsBaseUrl = null)
    {
        if (digest.Events.Count is < 1 or > 10) throw new ArgumentException("A digest must contain 1–10 tournaments.");
        var entries = digest.Events.Select((e, index) => new Entry(e, index, Brackets(e))).ToArray();
        // Validate all links, including links not displayed by the compact fallback.
        foreach (var e in entries)
        {
            ValidateSlug(e.Event.SignupSlug);
            foreach (var b in e.Brackets) ValidateSlug(b.SignupSlug);
        }
        var gameIds = entries.SelectMany(e => e.Brackets).Select(b => b.GameId).Distinct()
            .OrderBy(id => Array.IndexOf(digest.GameOrder ?? [], id) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(id => id).ToArray();
        if (gameIds.Length > 20) throw new ArgumentException("A timeline supports at most 20 selected games.");
        var compact = false;
        List<string> sections;
        string intro, footer;
        while (true)
        {
            intro = $"## {Text(digest.Name, 100)}\nUpcoming tournaments · {entries.Length} tournament{(entries.Length == 1 ? "" : "s")} · {gameIds.Length} game{(gameIds.Length == 1 ? "" : "s")}";
            if (compact) intro += "\n\n" + ParentDetails(entries[0].Event);
            footer = "-# Times shown in your timezone · Start times can change.";
            if (digest.HasMore) footer += digest.DeferredEditionIds is { Length: > 0 }
                ? " Remaining tournaments are queued for later notifications." : " More events match your filters.";
            sections = gameIds.Select(id => GameSection(entries, id, compact)).ToList();
            if (intro.Length + footer.Length + sections.Sum(s => s.Length) <= TextLimit) break;
            // Dense single tournaments still retain every bracket time and game, with one
            // shared tournament heading/link instead of repeating the venue in each section.
            if (entries.Length == 1 && !compact) { compact = true; continue; }
            throw new TimelineCapacityException();
        }
        var components = new List<object> { Display(intro) };
        // Each full-width gallery and text block consumes one component apiece.
        // Reserve the container, intro and footer before assigning artwork slots.
        var imageBudget = ComponentLimit - 3 - gameIds.Length;
        for (var i = 0; i < gameIds.Length; i++)
        {
            var image = GameHeaderCatalog.ImageUrl(gameIds[i], assetsBaseUrl);
            if (image != null && imageBudget > 0)
            {
                components.Add(new { type = 12,
                    items = new[] { new { media = new { url = image }, description = GameName(entries, gameIds[i]) } } });
                imageBudget--;
            }
            components.Add(Display(sections[i]));
        }
        components.Add(Display(footer));
        return new { flags = 32768, allowed_mentions = new { parse = Array.Empty<string>() },
            components = new[] { new { type = 17, accent_color = 0xD4475B, components } } };
    }

    private static object Display(string text) => new { type = 10, content = text };
    private static string GameSection(Entry[] entries, int gameId, bool compact)
    {
        var name = GameName(entries, gameId);
        var matches = entries.Where(e => e.Brackets.Any(b => b.GameId == gameId))
            .OrderBy(e => e.Brackets.Where(b => b.GameId == gameId).Min(b => EffectiveStart(e.Event, b) ?? e.Event.StartUtc))
            .ThenBy(e => e.Index).ToArray();
        var text = new StringBuilder($"### {Text(name, 60)}\n");
        foreach (var entry in matches)
        {
            var e = entry.Event;
            if (!compact) text.Append($"\n**{Text(e.Name, 140)}**\n{Text(e.Location, 80)}\n");
            var brackets = entry.Brackets.Where(b => b.GameId == gameId).OrderBy(b => EffectiveStart(e, b) ?? e.StartUtc).ThenBy(b => b.BracketId).ToArray();
            foreach (var bracket in brackets)
            {
                var effective = EffectiveStart(e, bracket);
                var time = effective.HasValue ? Stamp(effective.Value) : null;
                // Older queued snapshots contain only the known parent time.
                if (gameId == 0) time = Stamp(e.StartUtc);
                var count = bracket.Entrants?.ToString(CultureInfo.InvariantCulture) ?? "?";
                var label = brackets.Length > 1 ? BracketLabel(bracket) : "Register";
                text.Append("↳ ");
                if (time != null) text.Append(time).Append(" · ");
                text.Append($"current entrants: {count}");
                var slug = string.IsNullOrEmpty(bracket.SignupSlug) ? e.SignupSlug : bracket.SignupSlug;
                if (!compact && !string.IsNullOrEmpty(slug)) text.Append($" · [{label}]({DiscordEventFeedOutlet.SignupUrl(slug)})");
                else if (brackets.Length > 1) text.Append($" · {label}");
                text.Append('\n');
            }
        }
        return text.ToString().TrimEnd();
    }

    private static string ParentDetails(DigestEvent e) => $"**{Text(e.Name, 140)}**\n{Text(e.Location, 80)}"
        + (string.IsNullOrEmpty(e.SignupSlug) ? "" : $"\n[View all brackets / register]({DiscordEventFeedOutlet.SignupUrl(e.SignupSlug)})");
    private static string GameName(Entry[] entries, int id) => entries.SelectMany(e => e.Brackets).First(b => b.GameId == id).GameName;
    private static DateTime? EffectiveStart(DigestEvent e, DigestBracket b) => b.StartUtc.HasValue && b.StartUtc >= e.StartUtc
        && (!e.EndUtc.HasValue || e.EndUtc <= e.StartUtc || b.StartUtc <= e.EndUtc) ? b.StartUtc : null;
    private static string BracketLabel(DigestBracket b) => b.SignupSlug.Contains("/event/", StringComparison.Ordinal)
        ? Text(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(b.SignupSlug.Split('/').Last().Replace('-', ' ').Replace('_', ' ')), 70)
        : $"Bracket {b.BracketId}";
    private static IReadOnlyList<DigestBracket> Brackets(DigestEvent e) => e.Brackets is { Count: > 0 } ? e.Brackets
        : [new(0, "Tournament schedule", 0, e.SignupSlug, null, e.Entrants)];
    private static void ValidateSlug(string slug) { if (!string.IsNullOrEmpty(slug)) _ = DiscordEventFeedOutlet.SignupUrl(slug); }
    private static string Stamp(DateTime utc) => $"<t:{new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds()}:f>";
    private static string Text(string value, int max)
    {
        // Escape source-controlled Markdown and mentions, and remove format/control characters.
        var output = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format) continue;
            var c = rune.Value;
            var escaped = c is '\\' or '*' or '_' or '~' or '`' or '[' or ']' or '(' or ')' or '#' or '|' or '<' or '>' ? "\\" + rune : c == '@' ? "@\u200b" : rune.ToString();
            if (output.Length + escaped.Length > max) break;
            output.Append(escaped);
        }
        return output.ToString();
    }
    private sealed record Entry(DigestEvent Event, int Index, IReadOnlyList<DigestBracket> Brackets);
}

public sealed class TimelineCapacityException() : ArgumentException("A complete tournament exceeds Discord's single-message timeline limits.");
