using GraphQL.Client.Http;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Services.Players
{
    public class PlayerQueryService : IPlayerQueryService
    {
        private readonly GraphQLHttpClient _client;

        private readonly string _connectionString;
        public PlayerQueryService(string connectionString, GraphQLHttpClient graphQlClient)
        {

            _connectionString = connectionString;
            _client = graphQlClient;
        }
        public async Task<PlayerGraphQLResult> GetPlayerDataFromStartgg(IntakePlayersByTournamentCommand queryCommand)
        {
            return await QueryStartggPlayerData(queryCommand);
        }
        public async Task<PlayerStandingResult> QueryPlayerStandings(GetPlayerStandingsCommand command)
        {
            try
            {
                var data = await QueryStartggStandings(command);

                var newStandingResult = MapStandingsData(data);

                if(newStandingResult == null) { Console.WriteLine("No Data found for this Player"); throw new ArgumentNullException("No Data found for this Player"); }

                return newStandingResult;
            }
            catch (Exception ex)
            {
                return new PlayerStandingResult { Response = $"Failed: {ex.Message} - {ex.StackTrace}" };
            }
        }
        private PlayerStandingResult? MapStandingsData(StandingGraphQLResult data)
        {
            var tempNode = data.Data.Entrants.Nodes.FirstOrDefault();
            if (tempNode == null) return null;

            var mappedResult = new PlayerStandingResult
            {
                Response = "Open",
                EntrantsNum = tempNode.Standing.Container.NumEntrants,
                StandingDetails = new StandingDetails
                {
                    IsActive = tempNode.Standing.IsActive,
                    Placement = tempNode.Standing.Placement,
                    GamerTag = tempNode.Participants.FirstOrDefault().GamerTag,
                    EventId = tempNode.Standing.Container.Tournament.Id,
                    EventName = tempNode.Standing.Container.Tournament.Name,
                    TournamentId = tempNode.Standing.Container.Tournament.Id,
                    TournamentName = tempNode.Standing.Container.Name
                },
                TournamentLinks = new Links
                {
                    EntrantId = tempNode.Id,
                    StandingId = tempNode.Standing.Id
                }
            };

            return mappedResult;
        }
        private async Task<PlayerGraphQLResult> QueryStartggPlayerData(IntakePlayersByTournamentCommand command)
        {
            var tempQuery = @"query EventEntrants($perPage: Int!, $eventSlug: String!, $pageNum: Int) {
                                  event(slug: $eventSlug) {
                                    id
                                    name
                                    entrants(query: {perPage: $perPage, page: $pageNum filter: {}}) {
                                      nodes {
                                        id
                                        participants {id player{id gamerTag}}
                                        standing {id placement}}}}}";
            var request = new GraphQLHttpRequest
            {
                Query = tempQuery,
                Variables= new
                {
                    command.EventSlug,
                    command.PerPage,
                    command.PageNum
                }
            };
            try
            {
                var response = await _client.SendQueryAsync<JObject>(request);
                if (response.Data == null) throw new ApplicationException($"Failed to retrieve player data");

                var tempJson = JsonConvert.SerializeObject(response.Data, Formatting.Indented);

                var playerData = JsonConvert.DeserializeObject<PlayerGraphQLResult>(tempJson);
                return playerData;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message + ": " + ex.StackTrace);
                throw;
            }
        }
        private async Task<StandingGraphQLResult> QueryStartggStandings(GetPlayerStandingsCommand queryCommand)
        {
            var tempQuery = @"query EventEntrants($eventId: ID!, $perPage: Int!, $gamerTag: String!) {
                              event(id: $eventId) {
                                id
                                name
                                entrants(query: {
                                  perPage: $perPage
                                  filter: { name: $gamerTag }}) {
                                  nodes {id participants { id gamerTag } standing { id placement container {
                                        __typename
                                        ... on Tournament { id name countryCode startAt endAt events { id name }}
                                        ... on Event { id name startAt numEntrants tournament { id name }}
                                        ... on Set { id event { id name } startAt completedAt games { id }}
                                      }}}}}}";
            var request = new GraphQLHttpRequest
            {
                Query = tempQuery,
                Variables = new
                {
                    queryCommand.PerPage,
                    queryCommand.EventId,
                    queryCommand.GamerTag
                }
            };
            try
            {
                var response = await _client.SendQueryAsync<JObject>(request);
                if (response.Data == null) throw new ApplicationException($"Failed to retrieve standing data");

                var tempJson = JsonConvert.SerializeObject(response.Data, Formatting.Indented);

                var standingsData = JsonConvert.DeserializeObject<StandingGraphQLResult>(tempJson);
                return standingsData;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message + ": " + ex.StackTrace);
                throw;
            }
        }
    }
}
