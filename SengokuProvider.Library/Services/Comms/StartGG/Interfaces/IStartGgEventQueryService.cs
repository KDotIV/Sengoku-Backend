using SengokuProvider.Library.Models.Events;

namespace SengokuProvider.Library.Services.Comms.StartGG
{
    public interface IStartGgEventQueryService
    {
        Task<EventGraphQLResult?> GetTournamentByIdAsync(int tournamentId, CancellationToken cancellationToken = default);
        Task<EventGraphQLResult?> GetTournamentsByGameAsync(int perPage, string stateCode, int startDate, int endDate, int[] gameIds, CancellationToken cancellationToken = default);
        Task<EventGraphQLResult> GetTournamentsByStateAsync(int perPage, string stateCode, int startDate, int endDate, CancellationToken cancellationToken = default);
        Task<TournamentLinkGraphQLResult?> GetEventByIdAsync(int eventId, CancellationToken cancellationToken = default);
        Task<EventGraphQLResult?> GetEventDetailsAsync(int tournamentId, CancellationToken cancellationToken = default);
        Task<TournamentsBySlugGraphQLResult?> GetTournamentsBySlugAsync(string tournamentSlug, CancellationToken cancellationToken = default);
    }
}