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
        public async Task<PlayerGraphQLResult?> QueryPlayerDataFromStartgg(IntakePlayersByTournamentCommand queryCommand)
        {
            return await QueryStartggPlayerData(queryCommand);
        }
        public async Task<List<PlayerStandingResult>> QueryStartggPlayerStandings(int tournamentLink)
        {
            try
            {
                var data = await QueryStartggEventStandings(tournamentLink);

                var newStandingResults = MapStandingsData(data);

                if (newStandingResults.Count == 0) { Console.WriteLine("No Data found for this Tournament"); }

                return newStandingResults;
            }
            catch (Exception ex)
            {
                throw new ArgumentNullException($"Error found while Querying for Player Standings data {ex.Message} - {ex.StackTrace}");
            }
        }
        public async Task<List<PlayerStandingResult>> GetPlayerStandingResults(GetPlayerStandingsCommand command)
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

        private List<PlayerStandingResult> MapStandingsData(StandingGraphQLResult? data)
        {
            List<PlayerStandingResult> mappedResult = new List<PlayerStandingResult>();
            if (data == null) return mappedResult;
            foreach (var tempNode in data.Data.Entrants.Nodes)
            {
                if (tempNode.Standing == null) continue;

                var newStandings = new PlayerStandingResult
                {
                    Response = "Open",
                    EntrantsNum = data.Data.NumEntrants,
                    LastUpdated = DateTime.UtcNow,
                    UrlSlug = data.Data.Slug,
                    StandingDetails = new StandingDetails
                    {
                        IsActive = tempNode.Standing.IsActive,
                        Placement = tempNode.Standing.Placement,
                        GamerTag = tempNode.Participants.FirstOrDefault().GamerTag,
                        EventId = data.Data.Id,
                        EventName = data.Data.Name,
                        TournamentId = data.Data.Tournament.Id,
                        TournamentName = data.Data.Name
                    },
                    TournamentLinks = new Links
                    {
                        EntrantId = tempNode.Id,
                        StandingId = tempNode.Standing.Id
                    }
                };
                mappedResult.Add(newStandings);
            }
            return mappedResult;
        }
        private async Task<PlayerGraphQLResult?> QueryStartggPlayerData(IntakePlayersByTournamentCommand command, int perPage = 40)
        {
            //TODO: REMAP THIS QUERY TO THE COMMONPLAYERSCHEMA MODEL
            var tempQuery = @"query EventEntrants($perPage: Int!, $eventId: ID!) {
                    event(id: $eventId) {
                        id
                        name
                        tournament { id, name }
                        entrants(query: {perPage: $perPage, filter: {}}) {
                            nodes { id, participants { 
                                            id, player { 
                                                id, gamerTag },
                                                user {
                                                   id }} 
                                        standing { id, placement }}
                            pageInfo { total totalPages page perPage sortBy filter}}}}";

            var allNodes = new List<EntrantNode>();
            int currentTournamentId = 0;
            string currentTournamentName = "";
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
                        perPage,
                        eventId = command.TournamentLink,
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
                    },
                    Tournament = new Tournament
                    {
                        Id = currentTournamentId,
                        Name = currentTournamentName
                    },

                }
            };

            return result;
        }
        private async Task<StandingGraphQLResult?> QueryStartggEventStandings(int tournamentLink)
        {
            var tempQuery = @"query EventEntrants($eventId: ID!) {
                                event(id: $eventId) { id name numEntrants slug tournament {
                                  id name } entrants(query: {}) {
                                      nodes { id participants {
                                          id player { id gamerTag }} standing { id placement isFinal }}
                                    pageInfo { total totalPages page perPage sortBy filter }}}}";

            var request = new GraphQLHttpRequest
            {
                Query = tempQuery,
                Variables = new
                {
                    tournamentLink
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
