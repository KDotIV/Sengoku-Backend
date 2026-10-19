using SengokuProvider.Library.Models.Feeds;
namespace SengokuProvider.Library.Services.Events;
public interface IEventFeedQueryService
{
    Task<EventFeedPreview> GetUpcomingFeedEvents(EventFeedFilter filter, DateTime nowUtc, CancellationToken ct = default, int[]? tournamentIds = null);
}

