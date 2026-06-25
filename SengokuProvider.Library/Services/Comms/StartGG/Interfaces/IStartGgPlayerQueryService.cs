using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Models.User;

namespace SengokuProvider.Library.Services.Comms.StartGG.Interfaces
{
    public interface IStartGgPlayerQueryService
    {
        Task<PlayerGraphQLResult?> GetEventEntrantsAsync(int eventId, int perPage = 80, CancellationToken cancellationToken = default);
        Task<PhaseGroupGraphQL> GetPhaseGroupAsync(int phaseGroupId, int perPage = 50, CancellationToken cancellationToken = default);
        Task<PastEventPlayerData> GetPreviousEventsAsync(int playerId, string gamerTag, int perPage = 10, CancellationToken cancellationToken = default);
        Task<UserGraphQLResult?> GetUserAsync(string userSlug, CancellationToken cancellationToken = default);
    }
}