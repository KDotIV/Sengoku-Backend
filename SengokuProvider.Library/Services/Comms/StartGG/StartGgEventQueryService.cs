using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Services.Comms.StartGG.Interfaces;

namespace SengokuProvider.Library.Services.Comms.StartGG
{
    public class StartGgEventQueryService : IStartGgEventQueryService
    {
        private readonly IStartGgGraphQlClient _client;

        public StartGgEventQueryService(IStartGgGraphQlClient client) => _client = client;

        public Task<EventGraphQLResult?> GetTournamentByIdAsync(int tournamentId, CancellationToken cancellationToken = default) =>
            _client.QueryAsync<EventGraphQLResult>(
                """query TournamentQuery($tournamentId: ID) { tournaments(query: {filter: {id: $tournamentId}}) { nodes { id name addrState lat lng registrationClosesAt isRegistrationOpen city isOnline venueAddress startAt endAt slug events { id slug numEntrants videogame { id } } } } }""",
                new { tournamentId }, cancellationToken);

        public Task<EventGraphQLResult?> GetTournamentsByGameAsync(int perPage, string stateCode, int startDate, int endDate, int[] gameIds, CancellationToken cancellationToken = default) =>
            _client.QueryAsync<EventGraphQLResult>(
                """query TournamentQuery($perPage: Int, $videogameIds: [ID], $state: String!, $yearStart: Timestamp, $yearEnd: Timestamp) { tournaments(query: { perPage: $perPage filter: { videogameIds: $videogameIds addrState: $state afterDate: $yearStart beforeDate: $yearEnd } }) { nodes { id name slug startAt endAt events { id slug videogame { id } } } } }""",
                new { perPage, state = stateCode, yearStart = startDate, yearEnd = endDate, videogameIds = gameIds }, cancellationToken);

        public async Task<EventGraphQLResult> GetTournamentsByStateAsync(int perPage, string stateCode, int startDate, int endDate, CancellationToken cancellationToken = default)
        {
            const string query = """query TournamentQuery($perPage: Int, $pageNum: Int, $state: String!, $yearStart: Timestamp, $yearEnd: Timestamp) { tournaments(query: { perPage: $perPage page: $pageNum filter: { addrState: $state afterDate: $yearStart beforeDate: $yearEnd } }) { pageInfo { total totalPages } nodes { id name addrState lat lng registrationClosesAt isRegistrationOpen city isOnline venueAddress startAt endAt slug events { id slug numEntrants videogame { id } } } } }""";
            var nodes = new List<EventNode>();
            var page = 1;
            var totalPages = int.MaxValue;

            while (page <= totalPages)
            {
                var result = await _client.QueryAsync<EventGraphQLResult>(query, new { perPage, pageNum = page, state = stateCode, yearStart = startDate, yearEnd = endDate }, cancellationToken);
                if (result?.Events is null) break;
                nodes.AddRange(result.Events.Nodes ?? []);
                totalPages = result.Events.PageInfo?.TotalPages ?? 1;
                page++;
            }

            return new EventGraphQLResult { Events = new EventResult { Nodes = nodes } };
        }

        public Task<TournamentLinkGraphQLResult?> GetEventByIdAsync(int eventId, CancellationToken cancellationToken = default) =>
            _client.QueryAsync<TournamentLinkGraphQLResult>(
                """query GetEventById($id: ID) { event(id: $id) { id slug numEntrants videogame { id } tournament { id name addrState lat lng registrationClosesAt isRegistrationOpen city isOnline venueAddress startAt endAt slug } } }""",
                new { id = eventId }, cancellationToken);

        public Task<EventGraphQLResult?> GetEventDetailsAsync(int tournamentId, CancellationToken cancellationToken = default) =>
            _client.QueryAsync<EventGraphQLResult>(
                """query EventQuery($tournamentId: ID) { tournaments(query: {filter: {id: $tournamentId}}) { nodes { id name addrState lat lng registrationClosesAt isRegistrationOpen city isOnline venueAddress startAt endAt slug events { id slug numEntrants videogame { id } } } } }""",
                new { tournamentId }, cancellationToken);

        public Task<TournamentsBySlugGraphQLResult?> GetTournamentsBySlugAsync(string tournamentSlug, CancellationToken cancellationToken = default) =>
            _client.QueryAsync<TournamentsBySlugGraphQLResult>(
                """query TournamentQuery($tourneySlug: String) { tournament(slug: $tourneySlug) { id name slug events { id name slug numEntrants videogame { id name } } } }""",
                new { tourneySlug = tournamentSlug }, cancellationToken);
    }
}
