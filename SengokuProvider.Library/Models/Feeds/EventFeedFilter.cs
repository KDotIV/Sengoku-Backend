using System.Text.RegularExpressions;

namespace SengokuProvider.Library.Models.Feeds;

public sealed record EventFeedFilter
{
    public int[] GameIds { get; init; } = [];
    public string[] RegionIds { get; init; } = [];
    // Optional advanced override; normal integrations UI selects RegionIds from the catalogue.
    public string[] States { get; init; } = [];
    public string? City { get; init; }
    public string Attendance { get; init; } = "InPerson";
    public int LookAheadDays { get; init; } = 14;
    public int MinimumEntrants { get; init; }
    public bool RegistrationOpenOnly { get; init; } = true;
    public int MaxEvents { get; init; } = 10;

    // Structural limits protect the API. Selectable values are validated by the database catalogue.
    public EventFeedFilter Normalize()
    {
        if (GameIds == null || GameIds.Length is < 1 or > 20 || GameIds.Any(id => id <= 0))
            throw new ArgumentException("Select between 1 and 20 games.");
        if (RegionIds == null || RegionIds.Length > 60 || RegionIds.Any(s => s == null || !Regex.IsMatch(s, @"\A[a-z0-9-]{1,50}\z")))
            throw new ArgumentException("Invalid region selection.");
        if (States == null || States.Length > 60 || States.Any(s => s == null || !Regex.IsMatch(s.Trim(), @"\A[A-Za-z]{2}\z")))
            throw new ArgumentException("States must be two-letter codes from the catalogue.");
        if (Attendance is not ("InPerson" or "Online" or "Both")) throw new ArgumentException("Invalid attendance selection.");
        if (Attendance != "Online" && RegionIds.Length == 0 && States.Length == 0) throw new ArgumentException("Select at least one region.");
        if (LookAheadDays is < 1 or > 90 || MinimumEntrants is < 0 or > 1000000 || MaxEvents is < 1 or > 10 || City?.Length > 100)
            throw new ArgumentException("Invalid feed limits (1–90 days, 1–10 events).");
        return this with { GameIds = GameIds.Distinct().ToArray(), RegionIds = RegionIds.Distinct().ToArray(),
            States = States.Select(s => s.Trim().ToUpperInvariant()).Distinct().ToArray(), City = string.IsNullOrWhiteSpace(City) ? null : City.Trim() };
    }
}

public sealed class UpcomingFeedEvent
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string[]? StreamUrls { get; set; }
    public IReadOnlyList<BroadcastLink> Broadcasts => (StreamUrls ?? []).Select(BroadcastLink.Parse).OfType<BroadcastLink>().Distinct().Take(5).ToArray();
    public int TournamentId { get; set; }
    public int GameId { get; set; }
    public string Name { get; set; } = "";
    public string UrlSlug { get; set; } = "";
    public DateTime StartTimeUtc { get; set; }
    public DateTime? EndTimeUtc { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public bool IsOnline { get; set; }
    public int? Entrants { get; set; }
    public DateTime? LastUpdatedUtc { get; set; }
}
public sealed record EventFeedPreview(IReadOnlyList<UpcomingFeedEvent> Events, bool HasMore, DateTime GeneratedAtUtc);
