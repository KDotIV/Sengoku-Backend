using System.Globalization;

namespace SengokuProvider.Library.Models.Feeds;

public sealed record FeedSchedule
{
    public string Mode { get; init; } = "Interval";
    public int? IntervalHours { get; init; }
    public string? UtcTime { get; init; }
    public TimeSpan? Validate()
    {
        if (Mode == "Interval" && IntervalHours is >= 1 and <= 8760 && UtcTime == null) return null;
        if (Mode == "OnInsert" && IntervalHours == null && UtcTime == null) return null;
        if (Mode == "Daily" && IntervalHours == null && TimeOnly.TryParseExact(UtcTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return time.ToTimeSpan();
        throw new ArgumentException("Use Interval with intervalHours, Daily with utcTime (HH:mm), or OnInsert without time fields.");
    }
}
public sealed record SaveEventFeedRequest
{
    public string Name { get; init; } = "";
    public EventFeedFilter Filter { get; init; } = new();
    public FeedSchedule Schedule { get; init; } = null!;
    public string[] Providers { get; init; } = ["Discord"];
    public bool Enabled { get; init; }
    public int ExpectedVersion { get; init; }
}
public sealed record EventFeedDefinition(Guid Id, int CreatedByUserId, string Name, string[] Providers, EventFeedFilter Filter, FeedSchedule Schedule, bool Enabled, int Version);
public sealed record SubscribeEventFeedRequest(string Provider = "Discord");
public sealed record EventFeedSubscriptionView(Guid Id, string Name, string Provider, EventFeedFilter Filter, FeedSchedule Schedule,
    bool Enabled, DateTime? NextRunAtUtc, int Version, string ConnectionStatus, string? DiscordGuildId, string? DiscordChannelId)
{
    public Guid FeedId { get; init; }
}
public sealed class FeedConflictException(string message) : Exception(message);
internal sealed class FeedRow
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public Guid FeedId { get; set; }
    public string[] Providers { get; set; } = [];
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "Discord";
    public string? DestinationProtected { get; set; }
    public string Filters { get; set; } = "";
    public int FrequencyHours { get; set; }
    public string ScheduleMode { get; set; } = "Interval";
    public TimeSpan? DailyAtUtc { get; set; }
    public string? DiscordGuildId { get; set; }
    public string? DiscordChannelId { get; set; }
    public bool Enabled { get; set; }
    public DateTime NextRunAtUtc { get; set; }
    public int Version { get; set; }
}
public sealed class FeedDelivery
{
    public Guid Id { get; set; }
    public Guid FeedId { get; set; }
    public Guid SubscriptionId { get; set; }
    public string Status { get; set; } = "";
    public int Attempts { get; set; }
    public DateTime ScheduledFor { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? MessageId { get; set; }
    public string? Outcome { get; set; }
}
internal sealed class DispatchRow
{
    public Guid Id { get; set; }
    public Guid FeedId { get; set; }
    public string Payload { get; set; } = "";
    public string DestinationProtected { get; set; } = "";
    public int Attempts { get; set; }
}

public sealed record FeedGameOption(int Id, string Name);
public sealed record FeedRegionOption(string Id, string Name, string[] States);
public sealed record FeedScheduleOption(string Id, string Name, int? DefaultIntervalHours, bool RequiresUtcTime);
public sealed record FeedCatalogue(FeedGameOption[] Games, FeedRegionOption[] Regions, FeedScheduleOption[] Schedules);
