using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Models.User;
using SengokuProvider.Library.Services.Comms.StartGG.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SengokuProvider.Library.Services.Comms.StartGG
{
    public class StartGgPlayerQueryService : IStartGgPlayerQueryService
    {
        private readonly IStartGgGraphQlClient _client;

        public StartGgPlayerQueryService(IStartGgGraphQlClient client) => _client = client;

        public async Task<PlayerGraphQLResult?> GetEventEntrantsAsync(int eventId, int perPage = 80, CancellationToken cancellationToken = default)
        {
            const string query = """query EventEntrants($perPage: Int!, $pageNum: Int!, $eventId: ID!) { event(id: $eventId) { id name tournament { id name } slug numEntrants entrants(query: {perPage: $perPage, page: $pageNum, filter: {}}) { nodes { id paginatedSets(sortType: ROUND) { nodes { round displayScore winnerId } } participants { id player { id gamerTag } user { id } } standing { id placement isFinal } } pageInfo { total totalPages page perPage sortBy filter } } } }""";
            var nodes = new List<CommonEntrantNode>();
            CommonEventNode? eventNode = null;
            var page = 1;
            var totalPages = int.MaxValue;

            while (page <= totalPages)
            {
                var result = await _client.QueryAsync<PlayerGraphQLResult>(query, new { perPage, eventId, pageNum = page }, cancellationToken);
                eventNode = result?.TournamentLink;
                if (eventNode is null) break;
                nodes.AddRange(eventNode.Entrants.Nodes ?? []);
                totalPages = eventNode.Entrants.PageInfo?.TotalPages ?? 1;
                page++;
            }

            if (eventNode is null) return null;
            eventNode.Entrants = new CommonEntrantList { Nodes = nodes };
            return new PlayerGraphQLResult { TournamentLink = eventNode };
        }

        public async Task<PhaseGroupGraphQL> GetPhaseGroupAsync(int phaseGroupId, int perPage = 50, CancellationToken cancellationToken = default)
        {
            const string query = """query PhaseGroupSets($phaseGroupId: ID!, $page: Int!, $perPage: Int!) { phaseGroup(id: $phaseGroupId) { id displayIdentifier sets(page: $page, perPage: $perPage, sortType: STANDARD) { nodes { id slots { id entrant { id name } } } pageInfo { total totalPages page perPage sortBy filter } } } }""";
            var nodes = new List<SetNode>();
            PhaseGroup? phaseGroup = null;
            var page = 1;
            var totalPages = int.MaxValue;

            while (page <= totalPages)
            {
                var result = await _client.QueryAsync<PhaseGroupGraphQL>(query, new { perPage, phaseGroupId, page }, cancellationToken);
                phaseGroup = result?.PhaseGroup;
                if (phaseGroup is null) break;
                nodes.AddRange(phaseGroup.Sets.Nodes ?? []);
                totalPages = phaseGroup.Sets.PageInfo?.TotalPages ?? 1;
                page++;
            }

            phaseGroup ??= new PhaseGroup { Id = phaseGroupId };
            phaseGroup.Sets = new Sets { Nodes = nodes };
            return new PhaseGroupGraphQL { PhaseGroup = phaseGroup };
        }

        public async Task<PastEventPlayerData> GetPreviousEventsAsync(int playerId, string gamerTag, int perPage = 10, CancellationToken cancellationToken = default)
        {
            const string query = """query UserPreviousEventsQuery($playerId: ID!, $perPage: Int!, $playerName: String!, $pageNum: Int!) { player(id: $playerId) { id gamerTag user { id events(query: {page: $pageNum, perPage: $perPage, filter: {location: {countryCode: "US"}}}) { nodes { id name numEntrants slug tournament { id name } entrants(query: {filter: {name: $playerName}}) { nodes { id paginatedSets(sortType: ROUND) { nodes { round displayScore winnerId } } participants { id player { id gamerTag } } standing { id placement isFinal } } } } pageInfo { total totalPages page perPage } } } } }""";
            var nodes = new List<CommonEventNode>();
            var page = 1;
            var totalPages = int.MaxValue;

            while (page <= totalPages)
            {
                var result = await _client.QueryAsync<PastEventPlayerData>(query, new { playerId, perPage, playerName = gamerTag, pageNum = page }, cancellationToken);
                nodes.AddRange(result?.PlayerQuery?.User?.Events?.Nodes ?? []);
                totalPages = result?.PlayerQuery?.User?.Events?.PageInfo?.TotalPages ?? 1;
                page++;
            }

            return new PastEventPlayerData { PlayerQuery = new CommonPlayer { Id = playerId, GamerTag = gamerTag, User = new CommonUser { Events = new CommonEvents { Nodes = nodes } } } };
        }

        public Task<UserGraphQLResult?> GetUserAsync(string userSlug, CancellationToken cancellationToken = default) =>
            _client.QueryAsync<UserGraphQLResult>(
                """query UserQuery($userSlug: String) { user(slug: $userSlug) { id name slug player { id gamerTag } } }""",
                new { userSlug }, cancellationToken);
    }
}
