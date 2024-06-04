using GraphQL.Client.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Common;
using System.Net;

namespace SengokuProvider.Library.Services.Players
{
    public class PlayerQueryService : IPlayerQueryService
    {
        private readonly GraphQLHttpClient _client;
        private readonly string _connectionString;
        private readonly RequestThrottler _requestThrottler;

        public PlayerQueryService(string connectionString, GraphQLHttpClient graphQlClient, RequestThrottler throttler)
        {
            _requestThrottler = throttler;
            _connectionString = connectionString;
            _client = graphQlClient;
        }
        public async Task<PlayerGraphQLResult?> GetPlayerDataFromStartgg(IntakePlayersByTournamentCommand queryCommand)
        {
            return await QueryStartggPlayerData(queryCommand);
        }
        public async Task<PlayerStandingResult?> QueryPlayerStandings(GetPlayerStandingsCommand command)
        {
            try
            {
                var data = await QueryStartggStandings(command);

                var newStandingResult = MapStandingsData(data);

                if (newStandingResult == null) { Console.WriteLine("No Data found for this Player"); throw new ArgumentNullException("No Data found for this Player"); }

                return newStandingResult;
            }
            catch (Exception ex)
            {
                return new PlayerStandingResult { Response = $"Failed: {ex.Message} - {ex.StackTrace}", LastUpdated = DateTime.UtcNow };
            }
        }
        private PlayerStandingResult? MapStandingsData(StandingGraphQLResult? data)
        {
            if (data == null) return null;
            var tempNode = data.Data.Entrants.Nodes.FirstOrDefault();
            if (tempNode == null || tempNode.Standing == null) return null;

            var mappedResult = new PlayerStandingResult
            {
                Response = "Open",
                EntrantsNum = tempNode.Standing.Container.NumEntrants,
                LastUpdated = DateTime.UtcNow,
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
        private async Task<PlayerGraphQLResult?> QueryStartggPlayerData(IntakePlayersByTournamentCommand command)
        {
            var tempQuery = @"query EventEntrants($perPage: Int!, $eventSlug: String!, $pageNum: Int) {
                            event(slug: $eventSlug) {
                                id
                                name
                                entrants(query: {perPage: $perPage, page: $pageNum, filter: {}}) {
                                    nodes { id participants { id player { id gamerTag}}
                                        standing { id placement }}
                                    pageInfo { total totalPages page perPage sortBy filter}}}}";

            var allNodes = new List<EntrantNode>();
            int currentPage = 1;
            bool hasNextPage = true;
            string currentEventName = "";
            int currentEventId = 0;

            while (hasNextPage)
            {
                var request = new GraphQLHttpRequest
                {
                    Query = tempQuery,
                    Variables = new
                    {
                        perPage = command.PerPage,
                        eventSlug = command.EventSlug,
                        pageNum = currentPage
                    }
                };

                bool success = false;
                int retryCount = 0;
                const int maxRetries = 3;
                const int delay = 1000;

                while (!success && retryCount < maxRetries)
                {
                    await _requestThrottler.WaitIfPaused();

                    try
                    {
                        var response = await _client.SendQueryAsync<JObject>(request);

                        if (response.Errors != null && response.Errors.Any())
                        {
                            throw new ApplicationException($"GraphQL errors: {string.Join(", ", response.Errors.Select(e => e.Message))}");
                        }

                        if (response.Data == null)
                        {
                            throw new ApplicationException("Failed to retrieve player data");
                        }

                        var tempJson = JsonConvert.SerializeObject(response.Data, Formatting.Indented);
                        var playerData = JsonConvert.DeserializeObject<PlayerGraphQLResult>(tempJson);

                        if (playerData?.Data?.Entrants?.Nodes != null)
                        {
                            allNodes.AddRange(playerData.Data.Entrants.Nodes);
                        }

                        currentEventName = playerData.Data.Name;
                        currentEventId = playerData.Data.Id;
                        var pageInfo = response.Data["event"]["entrants"]["pageInfo"];
                        int totalPages = pageInfo["totalPages"].ToObject<int>();
                        currentPage = pageInfo["page"].ToObject<int>() + 1;

                        hasNextPage = currentPage <= totalPages;
                        success = true;
                    }
                    catch (GraphQLHttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var errorContent = ex.Content;
                        Console.WriteLine($"Rate limit exceeded: {errorContent}");
                        retryCount++;
                        if (retryCount >= maxRetries)
                        {
                            Console.WriteLine("Max retries reached. Pausing further requests.");
                            await _requestThrottler.PauseRequests();
                            throw;
                        }
                        Console.WriteLine($"Too many requests. Retrying in {delay}ms... Attempt {retryCount}/{maxRetries}");
                        await Task.Delay(delay);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message + ": " + ex.StackTrace);
                        return null;
                    }
                }
            }

            // Return the aggregated result
            var result = new PlayerGraphQLResult
            {
                Data = new Event
                {
                    Id = currentEventId,
                    Name = currentEventName,
                    Entrants = new EntrantList
                    {
                        Nodes = allNodes
                    }
                }
            };

            return result;
        }
        private async Task<StandingGraphQLResult?> QueryStartggStandings(GetPlayerStandingsCommand queryCommand)
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

            bool success = false;
            int retryCount = 0;
            const int maxRetries = 3;
            const int delay = 3000;

            while (!success && retryCount < maxRetries)
            {
                await _requestThrottler.WaitIfPaused();

                try
                {
                    var response = await _client.SendQueryAsync<JObject>(request);

                    if (response.Errors != null && response.Errors.Any())
                    {
                        throw new ApplicationException($"GraphQL errors: {string.Join(", ", response.Errors.Select(e => e.Message))}");
                    }

                    if (response.Data == null)
                    {
                        throw new ApplicationException("Failed to retrieve standing data");
                    }

                    var tempJson = JsonConvert.SerializeObject(response.Data, Formatting.Indented);
                    var standingsData = JsonConvert.DeserializeObject<StandingGraphQLResult>(tempJson);
                    success = true;
                    return standingsData;
                }
                catch (GraphQLHttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests || ex.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    var errorContent = ex.Content;
                    Console.WriteLine($"Rate limit exceeded: {errorContent}");
                    retryCount++;
                    if (retryCount >= maxRetries)
                    {
                        Console.WriteLine("Max retries reached. Pausing further requests.");
                        await _requestThrottler.PauseRequests();
                        throw;
                    }
                    Console.WriteLine($"Too many requests. Retrying in {delay}ms... Attempt {retryCount}/{maxRetries}");
                    await Task.Delay(delay);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message + ": " + ex.StackTrace);
                    return null;
                }
            }
            throw new ApplicationException("Failed to retrieve standing data after multiple attempts.");
        }
    }
}
