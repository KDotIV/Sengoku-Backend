using SengokuProvider.Library.Models.Feeds;
namespace SengokuProvider.Library.Services.Events;
public interface IEventFeedQueryService
{
    Task<EventFeedPreview> GetEditionFeedEvents(EventFeedFilter filter, DateTime nowUtc, int[]? editionIds, Guid subscriptionId, CancellationToken ct);
    Task<EventFeedPreview> GetUpcomingFeedEvents(EventFeedFilter filter, DateTime nowUtc, CancellationToken ct = default, int[]? tournamentIds = null);
}

