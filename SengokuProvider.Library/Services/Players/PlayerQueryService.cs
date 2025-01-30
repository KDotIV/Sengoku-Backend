using GraphQL.Client.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using System.Net;
using System.Net.Http.Headers;

namespace SengokuProvider.Library.Services.Players
{
    public class PlayerQueryService : IPlayerQueryService
    {
        private readonly GraphQLHttpClient _client;
        private readonly string _connectionString;
        private readonly RequestThrottler _requestThrottler;
        private readonly ICommonDatabaseService _commonDatabaseServices;
        private readonly IConfiguration _configuration;

        public PlayerQueryService(string connectionString, IConfiguration config, GraphQLHttpClient graphQlClient, RequestThrottler throttler, ICommonDatabaseService commonServices)
        {
            _requestThrottler = throttler;
            _connectionString = connectionString;
            _configuration = config;
            _client = graphQlClient;
            _commonDatabaseServices = commonServices;
            _client.HttpClient.DefaultRequestHeaders.Clear();
            _client.HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _configuration["GraphQLSettings:PlayerBearer"]);
        }
        public async Task<List<PlayerData>> GetRegisteredPlayersByTournamentId(int tournamentId)
        {
            if (tournamentId == 0 || tournamentId < 0) { List<PlayerData> badResult = new List<PlayerData>(); Console.WriteLine("TournamentId cannot be invalid"); return badResult; }

            var result = await QueryPlayerDataByTournamentId(tournamentId);

            return result;
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
        public async Task<List<PlayerStandingResult>> GetPlayerStandingsCodexModule(int[] playerIds, int[] tournamentIds, int[] gameIds, DateTime startDate = default, DateTime endDate = default)
        {
            var playerResults = new List<PlayerStandingResult>();

            if (playerIds.Length < 1) return playerResults;
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT standings.entrant_id, standings.player_id, standings.tournament_link, 
                                                        standings.placement, standings.entrants_num,
                                                        players.player_name, tournament_links.event_id,
                                                        tournament_links.url_slug, events.event_name, events.end_time, standings.last_updated
                                                        FROM standings
                                                        JOIN players ON standings.player_id = players.id
                                                        JOIN tournament_links ON standings.tournament_link = tournament_links.id
                                                        JOIN events ON events.link_id = tournament_links.event_id
                                                        WHERE standings.player_id = @PlayerArray 
                                                        and tournament_links.game_id = @TournamentsArray
                                                        and start_time = @StartTimeInput
                                                        and end_time = @EndTimeInput
                                                        and tournament_links.game_id = @GamesArray
                                                        ORDER BY players.player_name, events.end_time ASC;", conn))
                    {
                        cmd.Parameters.Add(_commonDatabaseServices.CreateDBIntArrayType("@TournamentsArray", tournamentIds));
                        cmd.Parameters.Add(_commonDatabaseServices.CreateDBIntArrayType("@PlayerArray", playerIds));
                        cmd.Parameters.AddWithValue("@StartTimeInput", startDate);
                        cmd.Parameters.AddWithValue("@EndTimeInput", endDate);
                        cmd.Parameters.AddWithValue("@GamesArray", gameIds);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows)
                            {
                                Console.WriteLine("No standings were found for given Ids. Check parameters...");
                                return playerResults;
                            }
                            while (await reader.ReadAsync())
                            {
                                playerResults.Add(ParseStandingsRecords(reader));
                            }
                        }
                    };
                }
                return playerResults;
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
        private List<PlayerStandingResult> MapStandingsData(PlayerGraphQLResult? data)
        {
            List<PlayerStandingResult> mappedResult = new List<PlayerStandingResult>();
            if (data == null) return mappedResult;
            foreach (var tempNode in data.TournamentLink.Entrants.Nodes)
            {
                if (tempNode.Standing == null) continue;
                int numEntrants = data.TournamentLink.NumEntrants ?? 0;

                var newStandings = new PlayerStandingResult
                {
                    Response = "Open",
                    EntrantsNum = numEntrants,
                    LastUpdated = DateTime.UtcNow,
                    UrlSlug = data.TournamentLink.Slug,
                    StandingDetails = new StandingDetails
                    {
                        IsActive = tempNode.Standing.IsActive ?? false,
                        Placement = tempNode.Standing.Placement ?? 0,
                        GamerTag = tempNode.Participants?.FirstOrDefault()?.Player.GamerTag ?? "",
                        EventId = data.TournamentLink.EventLink.Id,
                        EventName = data.TournamentLink.EventLink.Name,
                        TournamentId = data.TournamentLink.Id,
                        TournamentName = data.TournamentLink.Name
                    },
                    TournamentLinks = new Links
                    {
                        EntrantId = tempNode.Id,
                        StandingId = tempNode.Standing.Id,
                        PlayerId = tempNode.Participants?.FirstOrDefault()?.Player?.Id ?? 0,
                    }
                };
                mappedResult.Add(newStandings);
            }
            return mappedResult;
        }
        private async Task<PlayerGraphQLResult> QueryStartggPlayerData(IntakePlayersByTournamentCommand command, int perPage = 40)
        {
            var tempQuery = @"query EventEntrants($perPage: Int!, $eventId: ID!) {
                    event(id: $eventId) {
                        id
                        name
                        tournament { id, name }
                        slug
                        numEntrants
                        entrants(query: {perPage: $perPage, filter: {}}) {
                            nodes { id, paginatedSets(sortType: ROUND) {
                                            nodes { round, displayScore, winnerId }},
                                        participants { 
                                            id, player { 
                                                id, gamerTag },
                                                user {
                                                   id }} 
                                        standing { id, placement, isFinal }}
                            pageInfo { total totalPages page perPage sortBy filter}}}}";

            var jsonSerializerSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Include,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate
            };

            var allNodes = new List<CommonEntrantNode>();
            string currentEventLinkName = string.Empty;
            int currentPage = 1;
            int totalPages = int.MaxValue;
            string currentTournamentLinkName = string.Empty;
            int currentTournamentLinkId = 0;
            int currentEntrantsNum = 0;
            string currentTournamentLinkSlug = string.Empty;

            for (; currentPage <= totalPages; currentPage++)
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
                        var playerData = JsonConvert.DeserializeObject<PlayerGraphQLResult>(tempJson, jsonSerializerSettings);

                        if (playerData == null || playerData.TournamentLink == null)
                        {
                            Console.WriteLine("Failed to retrieve player data");
                            totalPages = 0;
                            break;
                        }

                        if (playerData.TournamentLink.Entrants.Nodes != null)
                        {
                            allNodes.AddRange(playerData.TournamentLink.Entrants.Nodes);
                            Console.WriteLine("Tournament Node Added");
                        }

                        currentEventLinkName = playerData.TournamentLink.EventLink.Name;
                        currentTournamentLinkName = playerData.TournamentLink.Name ?? string.Empty;
                        currentTournamentLinkId = playerData.TournamentLink?.Id ?? 0;
                        currentEntrantsNum = playerData.TournamentLink?.NumEntrants ?? 0;
                        currentTournamentLinkSlug = playerData.TournamentLink?.Slug ?? string.Empty;

                        // Update pagination info for the next iteration
                        var pageInfo = playerData?.TournamentLink?.Entrants?.PageInfo;
                        if (pageInfo != null)
                        {
                            totalPages = pageInfo.TotalPages ?? 1;
                            Console.WriteLine($"Current PlayerStandings Page: {currentPage}/{totalPages}");
                        }
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
                    }
                }
            }

            // Return the aggregated result
            var result = new PlayerGraphQLResult
            {
                TournamentLink = new CommonEventNode
                {
                    Id = command.TournamentLink,
                    Name = currentTournamentLinkName,
                    Slug = currentTournamentLinkSlug,
                    NumEntrants = currentEntrantsNum,
                    Entrants = new CommonEntrantList
                    {
                        Nodes = allNodes
                    },
                    EventLink = new CommonTournament
                    {
                        Id = currentTournamentLinkId,
                        Name = currentTournamentLinkName
                    }
                }
            };

            return result;
        }
        private async Task<PlayerGraphQLResult?> QueryStartggEventStandings(int tournamentLink)
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

            var jsonSerializerSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Include,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate
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
                    var standingsData = JsonConvert.DeserializeObject<PlayerGraphQLResult>(tempJson, jsonSerializerSettings);
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
        public async Task<PastEventPlayerData> QueryStartggPreviousEventData(int playerId, string gamerTag, int perPage = 10)
        {
            var tempQuery = @"query UserPreviousEventsQuery($playerId: ID!, $perPage: Int!, $playerName: String!) {
                                  player(id: $playerId) {id, gamerTag
                                    user { id
                                      events(query: {perPage: $perPage, filter: {location: {countryCode: ""US""}}}) {
                                        nodes { id name numEntrants slug
                                          tournament { id name }
                                          entrants(query: { filter: {name: $playerName}}) {
                                            nodes { id
                                              paginatedSets(sortType: ROUND) {
                                                nodes { round displayScore winnerId }}
                                              participants { id player { id gamerTag }}
                                              standing { id placement isFinal }}}}
                                        pageInfo { total totalPages page perPage}}}}}";

            var allNodes = new List<CommonEventNode>();
            int currentPage = 1;
            int totalPages = int.MaxValue; // Initialize to a large number

            for (; currentPage <= totalPages; currentPage++)
            {
                var request = new GraphQLHttpRequest
                {
                    Query = tempQuery,
                    Variables = new
                    {
                        playerId,
                        perPage,
                        playerName = gamerTag,
                        pageNum = currentPage
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

                        if (playerData?.PlayerQuery?.User?.Events?.Nodes != null)
                        {
                            allNodes.AddRange(playerData.PlayerQuery.User.Events.Nodes);
                            Console.WriteLine("Tournament Node Added");
                        }

                        // Update pagination info for the next iteration
                        var pageInfo = playerData?.PlayerQuery?.User?.Events?.PageInfo;
                        if (pageInfo != null)
                        {
                            totalPages = pageInfo.TotalPages ?? 1;
                            Console.WriteLine($"Current PlayerStandings Page: {currentPage}/{totalPages}");
                        }

                        success = true;
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
                    }
                }
            }

            // Construct the final result using the paginated results
            var result = new PastEventPlayerData
            {
                PlayerQuery = new CommonPlayer
                {
                    Id = playerId,
                    GamerTag = gamerTag,
                    User = new CommonUser
                    {
                        Events = new CommonEvents
                        {
                            Nodes = allNodes
                        }
                    }
                }
            };
            return result;
        }
        private async Task<List<PlayerData>> QueryPlayerDataByTournamentId(int tournamentLink)
        {
            List<PlayerData> playerResult = new List<PlayerData>();
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"select p.id, p.player_name, p.startgg_link, p.user_link, s.last_updated
                                                            FROM players as p 
                                                            JOIN standings as s ON s.player_id = p.id
                                                            JOIN tournament_links as t ON t.id = s.tournament_link
                                                            where t.id = @Input
                                                            ORDER BY player_name ASC;", conn))
                    {
                        cmd.Parameters.AddWithValue("@Input", tournamentLink);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows)
                            {
                                Console.WriteLine("No players found with that TournamentLink Id");
                                return playerResult;
                            }
                            while (await reader.ReadAsync())
                            {
                                playerResult.Add(new PlayerData
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                                    PlayerName = reader.GetString(reader.GetOrdinal("player_name")),
                                    UserLink = reader.GetInt32(reader.GetOrdinal("user_link")),
                                    PlayerLinkID = reader.GetInt32(reader.GetOrdinal("startgg_link")),
                                    LastUpdate = reader.GetDateTime(reader.GetOrdinal("last_updated"))
                                });
                            }
                        }
                    }
                }
                return playerResult;
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
    }
}
