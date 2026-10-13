using SengokuProvider.Library.Models.Feeds;
using SengokuProvider.Library.Services.Events;

namespace SengokuProvider.Library.Services.Feeds;

public interface IEventFeedStore
{
    Task<FeedCatalogue> Options(CancellationToken ct);
    Task<IReadOnlyList<EventFeedView>> List(int userId, CancellationToken ct);
    Task<EventFeedView?> Get(int userId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<FeedDelivery>> History(int userId, Guid id, CancellationToken ct);
    Task<EventFeedView?> Save(int userId, Guid? id, SaveEventFeedRequest request, CancellationToken ct);
    Task<bool> Delete(int userId, Guid id, CancellationToken ct);
}

public interface IEventFeedDispatcher
{
    Task<int> DiscoverNewTournaments(CancellationToken ct);
    Task<bool> PrepareNext(IEventFeedQueryService events, CancellationToken ct);
    Task<bool> DispatchNext(IEventFeedOutlet outlet, CancellationToken ct);
    Task<int> RecoverInterrupted(CancellationToken ct);
}


