using SengokuDispatcher.Library.Models.Feeds;
using SengokuProvider.Library.Models.Feeds;
using SengokuProvider.Library.Services.Events;

namespace SengokuDispatcher.Library.Services.Feeds;

public interface IEventFeedStore
{
    Task<EventFeedSubscriptionView?> Subscribe(int userId, Guid feedId, string provider, CancellationToken ct);
    Task<EventFeedSubscriptionView?> GetSubscription(int userId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<EventFeedSubscriptionView>> Subscriptions(int userId, CancellationToken ct);
    Task<bool> Unsubscribe(int userId, Guid id, CancellationToken ct);
    Task<FeedCatalogue> Options(CancellationToken ct);
    Task<IReadOnlyList<EventFeedDefinition>> List(int userId, CancellationToken ct);
    Task<EventFeedDefinition?> Get(int userId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<FeedDelivery>> History(int userId, Guid id, CancellationToken ct);
    Task<EventFeedDefinition?> Save(int userId, Guid? id, SaveEventFeedRequest request, CancellationToken ct);
    Task<bool> Delete(int userId, Guid id, CancellationToken ct);
}

public interface IEventFeedDispatcher
{
    Task<int> DiscoverNewTournaments(CancellationToken ct);
    Task<bool> PrepareNext(IEventFeedQueryService events, CancellationToken ct);
    Task<bool> DispatchNext(IEventFeedOutlet outlet, CancellationToken ct);
    Task<int> RecoverInterrupted(CancellationToken ct);
}


