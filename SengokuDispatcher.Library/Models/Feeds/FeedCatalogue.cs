namespace SengokuDispatcher.Library.Models.Feeds;

public sealed record FeedGameOption(int Id, string Name);
public sealed record FeedRegionOption(string Id, string Name, string[] States);
public sealed record FeedScheduleOption(string Id, string Name, int? DefaultIntervalHours, bool RequiresUtcTime);
public sealed record FeedCatalogue(FeedGameOption[] Games, FeedRegionOption[] Regions, FeedScheduleOption[] Schedules);
