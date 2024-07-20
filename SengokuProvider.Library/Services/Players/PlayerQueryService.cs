using GraphQL.Client.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Common;
using System.Net;
using System.Net.Http.Headers;

namespace SengokuProvider.Library.Services.Players
{
    public class PlayerQueryService : IPlayerQueryService
    {
        private readonly GraphQLHttpClient _client;
        private readonly string _connectionString;
        private readonly RequestThrottler _requestThrottler;
        private readonly IConfiguration _configuration;

        public PlayerQueryService(string connectionString, IConfiguration config, GraphQLHttpClient graphQlClient, RequestThrottler throttler)
        {
            _requestThrottler = throttler;
            _connectionString = connectionString;
            _configuration = config;
            _client = graphQlClient;
            _client.HttpClient.DefaultRequestHeaders.Clear();
            _client.HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _configuration["GraphQLSettings:PlayerBearer"]);
        }
        public async Task<PlayerGraphQLResult?> GetPlayerDataFromStartgg(IntakePlayersByTournamentCommand queryCommand)
        {
            return await QueryStartggPlayerData(queryCommand);
        }
        public async Task<PlayerStandingResult?> QueryStartggPlayerStandings(GetPlayerStandingsCommand command)
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
        public async Task<List<PlayerStandingResult>> GetPlayerStandingResults(QueryPlayerStandingsCommand command)
        {
            List<PlayerStandingResult> playerStandingResults = new List<PlayerStandingResult>();
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT standings.entrant_id, standings.player_id, standings.tournament_link, 
                                                        standings.placement, standings.entrants_num, standings.active, standings.last_updated,
                                                        players.player_name, tournament_links.event_id, tournament_links.id, tournament_links.url_slug, 
                                                        events.event_name
                                                        FROM standings
                                                        JOIN players ON standings.player_id = players.id
                                                        JOIN tournament_links ON standings.tournament_link = tournament_links.id
                                                        JOIN events ON events.link_id = tournament_links.id
                                                        WHERE standings.player_id = @Input
                                                        ORDER BY standings.active DESC;", conn))
                    {
                        cmd.Parameters.AddWithValue("@Input", command.PlayerId);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows)
                            {
                                Console.WriteLine("No standings found for the provided player ID.");
                                return playerStandingResults;
                            }
                            while (await reader.ReadAsync())
                            {
                                playerStandingResults.Add(ParseStandingsRecords(reader));
                            }
                        }
                    }
                }
                return playerStandingResults;
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException("Database error occurred: ", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Unexpected Error Occurred: ", ex);
            }
        }
        private PlayerStandingResult ParseStandingsRecords(NpgsqlDataReader reader)
        {
            return new PlayerStandingResult
            {
                StandingDetails = new StandingDetails
                {
                    IsActive = reader.IsDBNull(reader.GetOrdinal("active")) ? false : reader.GetBoolean(reader.GetOrdinal("active")),
                    Placement = reader.IsDBNull(reader.GetOrdinal("placement")) ? 0 : reader.GetInt32(reader.GetOrdinal("placement")),
                    GamerTag = reader.IsDBNull(reader.GetOrdinal("player_name")) ? "" : reader.GetString(reader.GetOrdinal("player_name")),
                    EventId = reader.IsDBNull(reader.GetOrdinal("event_id")) ? 0 : reader.GetInt32(reader.GetOrdinal("event_id")),
                    EventName = reader.IsDBNull(reader.GetOrdinal("event_name")) ? "" : reader.GetString(reader.GetOrdinal("event_name")),
                    TournamentId = reader.IsDBNull(reader.GetOrdinal("id")) ? 0 : reader.GetInt32(reader.GetOrdinal("id")),
                    TournamentName = reader.IsDBNull(reader.GetOrdinal("url_slug")) ? "" : reader.GetString(reader.GetOrdinal("url_slug"))
                },
                TournamentLinks = new Links
                {
                    EntrantId = reader.GetInt32(reader.GetOrdinal("entrant_id")),
                    PlayerId = reader.GetInt32(reader.GetOrdinal("player_id"))
                },
                EntrantsNum = reader.IsDBNull(reader.GetOrdinal("entrants_num")) ? 0 : reader.GetInt32(reader.GetOrdinal("entrants_num")),
                LastUpdated = reader.IsDBNull(reader.GetOrdinal("last_updated")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("last_updated")),
            };
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
                            await _requestThrottler.PauseRequests(_client);
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
                        await _requestThrottler.PauseRequests(_client);
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
        public async Task<PastEventPlayerData?> QueryStartggPreviousEventData(OnboardPlayerDataCommand queryCommand)
        {
            var tempQuery = @"query PlayerQuery($playerId: ID!){
                            player(id: $playerId){
                            id,
                            gamerTag,
                            user {
                                id,
                                events(query: {
                                filter: { minEntrantCount: 30, location: { countryCode:""US"" }}})
                                { nodes { id, name}}}}}";

            var request = new GraphQLHttpRequest
            {
                Query = tempQuery,
                Variables = new
                {
                    queryCommand.PlayerId
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
                    var playerData = JsonConvert.DeserializeObject<PastEventPlayerData>(tempJson);
                    success = true;
                    return playerData;
                }
                catch (GraphQLHttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests || ex.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    var errorContent = ex.Content;
                    Console.WriteLine($"Rate limit exceeded: {errorContent}");
                    retryCount++;
                    if (retryCount >= maxRetries)
                    {
                        Console.WriteLine("Max retries reached. Pausing further requests.");
                        await _requestThrottler.PauseRequests(_client);
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
