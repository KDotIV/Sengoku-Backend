﻿using Dapper;
using GraphQL.Client.Http;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Models.User;
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
        public async Task<List<PlayerData>> GetRegisteredPlayersByTournamentId(int[] tournamentIds)
        {
            if (tournamentIds.Length == 0 || tournamentIds.Length < 0) { List<PlayerData> badResult = new List<PlayerData>(); Console.WriteLine("TournamentId cannot be invalid"); return badResult; }

            var result = await QueryPlayerDataByTournamentId(tournamentIds);

            return result;
        }
        public async Task<PlayerGraphQLResult?> QueryPlayerDataFromStartgg(int tournamentLink)
        {
            return await QueryStartggPlayerData(tournamentLink);
        }
        public async Task<PhaseGroupGraphQL> QueryBracketDataFromStartggByBracketId(int bracketId)
        {
            return await QueryPhaseGroupDataByID(bracketId);
        }
        public async Task<List<PlayerStandingResult>> GetStandingsDataByPlayerIds(int[] playerIds, int[] tournamentIds)
        {
            return await QueryStandingsByPlayerIds(playerIds, tournamentIds);
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
                                                        players.player_name, tournament_links.event_link, tournament_links.id, tournament_links.url_slug, 
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
                                                        players.player_name, tournament_links.event_link,
                                                        tournament_links.url_slug, events.event_name, events.end_time, standings.last_updated
                                                        FROM standings
                                                        JOIN players ON standings.player_id = players.id
                                                        JOIN tournament_links ON standings.tournament_link = tournament_links.id
                                                        JOIN events ON events.link_id = tournament_links.event_link
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
                    }
                    ;
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
        public async Task<List<PlayerTournamentCard>> GetTournamentCardsByPlayerIDs(int[] playerIds)
        {
            if (playerIds.Length < 1) throw new ArgumentException("playerIds cannot be empty or invalid array");

            var playerCards = new List<PlayerTournamentCard>();
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    const string sql = @"SELECT * FROM get_player_standings(@PlayerIds)";

                    var flatRows = await conn.QueryAsync<FlatPlayerStandings>(sql, new { PlayerIds = playerIds });

                    playerCards = flatRows.GroupBy(r => r.PlayerID)
                        .Select(g =>
                        {
                            var firstRecord = g.First();
                            return new PlayerTournamentCard
                            {
                                PlayerID = g.Key,
                                PlayerName = firstRecord.PlayerName,
                                PlayerResults = g.Take(10) //takes the top 10 from player
                                .Select(r => new PlayerStandingResult
                                {
                                    StandingDetails = new StandingDetails
                                    {
                                        GamerTag = firstRecord.PlayerName,
                                        TournamentId = r.Tournament_Link,
                                        Placement = r.Placement
                                    },
                                    LastUpdated = r.LastUpdated,
                                    EntrantsNum = r.EntrantsNum,
                                }).ToList()
                            };
                        }).ToList();
                }
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException("Database error occurred: ", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Unexpected Error Occurred: ", ex);
            }

            return playerCards;
        }
        public async Task<UserPlayerData> GetUserDataByUserLink(int userLink)
        {
            return await QueryUserDataByUserLink(userLink);
        }
        public async Task<UserPlayerData> GetUserDataByUserSlug(string userSlug)
        {
            var result = new UserPlayerData
            {
                PlayerId = 0,
                PlayerEmail = "",
                PlayerName = "",
                userLink = 0,
                GameIds = []
            };
            try
            {
                var queryResult = await QueryStartggUserData(userSlug);
                if (queryResult == null) return result;

                return await MapUserPlayerDataAsync(result, queryResult);
            }
            catch (Exception)
            {

                throw;
            }
        }
        public Task<UserPlayerData> GetUserDataByPlayerName(string playerName)
        {
            throw new NotImplementedException();
        }
        public async Task<PlayerData> GetPlayerByName(string playerName)
        {
            var result = new PlayerData
            {
                Id = 0,
                PlayerName = "",
                PlayerLinkID = 0,
                UserLink = 0,
                LastUpdate = DateTime.UtcNow
            };
            return await QueryPlayersByPlayerName(playerName, result);
        }
        public async Task<List<Links>> GetPlayersByEntrantLinks(int[] entrantId)
        {
            return await QueryPlayersByEntrantLinks(entrantId);
        }
        private async Task<List<Links>> QueryPlayersByEntrantLinks(int[] entrantIds)
        {
            if (entrantIds.Length < 1)
            {
                Console.WriteLine("Entrant IDs cannot be empty or invalid array");
                return new List<Links>();
            }
            var result = new List<Links>();
            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                using (var cmd = new NpgsqlCommand(@"SELECT player_id, entrant_id FROM standings WHERE entrant_id = ANY(@entrantIds)", conn))
                {
                    cmd.Parameters.AddWithValue("entrantIds", entrantIds);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (!reader.HasRows)
                        {
                            Console.WriteLine("No player found for the provided entrant ID.");
                            return result;
                        }
                        while (await reader.ReadAsync())
                        {
                            result.Add(new Links
                            {
                                PlayerId = reader.GetInt32(reader.GetOrdinal("player_id")),
                                EntrantId = reader.GetInt32(reader.GetOrdinal("entrant_id"))
                            });
                        }
                    }
                }
                return result;
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.InnerException}", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        private async Task<List<PlayerStandingResult>> QueryStandingsByPlayerIds(int[] playerIds, int[] tournamentIds)
        {
            if (playerIds.Length < 1 || tournamentIds.Length < 1)
            {
                Console.WriteLine("Player or Tournament Ids cannot be empty or invalid array");
                return new List<PlayerStandingResult>();
            }
            var playerResults = new List<PlayerStandingResult>();

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT * FROM standings 
                                                            WHERE player_id = ANY(@playerIds) 
                                                            AND tournament_link = ANY(@tournamentIds);", conn))
                    {
                        cmd.Parameters.AddWithValue("playerIds", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer, playerIds);
                        cmd.Parameters.AddWithValue("tournamentIds", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer, tournamentIds);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows)
                            {
                                Console.WriteLine("No standings found for the provided player IDs and tournament IDs.");
                                return playerResults;
                            }
                            while (await reader.ReadAsync())
                            {
                                playerResults.Add(new PlayerStandingResult
                                {
                                    EntrantsNum = reader.GetInt32(reader.GetOrdinal("entrants_num")),

                                    StandingDetails = new StandingDetails
                                    {
                                        IsActive = reader.GetBoolean(reader.GetOrdinal("active")),
                                        Placement = reader.GetInt32(reader.GetOrdinal("placement")),
                                        TournamentId = reader.GetInt32(reader.GetOrdinal("tournament_link"))
                                    },
                                    TournamentLinks = new Links
                                    {
                                        EntrantId = reader.GetInt32(reader.GetOrdinal("entrant_id")),
                                        PlayerId = reader.GetInt32(reader.GetOrdinal("player_id"))
                                    },
                                    LastUpdated = reader.GetDateTime(reader.GetOrdinal("last_updated"))
                                });
                            }
                            return playerResults;
                        }
                    }
                }
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.InnerException}", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        private async Task<PlayerData> QueryPlayersByPlayerName(string playerName, PlayerData result)
        {
            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                const string sql = @"SELECT * FROM public.players where player_name like '%@PlayerName%';";
                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@PlayerName", playerName);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        if (!reader.HasRows) return result;

                        result.Id = reader.GetInt32(reader.GetOrdinal("id"));
                        result.PlayerName = reader.GetString(reader.GetOrdinal("player_name"));
                        result.PlayerLinkID = reader.GetInt32(reader.GetOrdinal("startgg_link"));
                        result.UserLink = reader.GetInt32(reader.GetOrdinal("user_link"));
                        result.LastUpdate = reader.GetDateTime(reader.GetOrdinal("last_updated"));
                    }
                    return result;
                }
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.InnerException}", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        private async Task<UserPlayerData> MapUserPlayerDataAsync(UserPlayerData result, UserGraphQLResult queryResult)
        {
            if (queryResult.UserNode.Player == null) return result;

            int existing = await CheckExistingPlayerbase(queryResult.UserNode.Player.Id);

            if (existing > 0) { result.PlayerId = existing; }
            else { result.PlayerId = queryResult.UserNode.Player.Id; }
            result.PlayerName = queryResult.UserNode.Player.GamerTag ?? "";
            result.userLink = queryResult.UserNode.Id;

            return result;
        }
        private async Task<int> CheckExistingPlayerbase(int playerLinkId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            try
            {
                await conn.OpenAsync();

                using var cmd = new NpgsqlCommand(@"SELECT * FROM players where startgg_link = @Input", conn);
                cmd.Parameters.AddWithValue("@Input", playerLinkId);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        if (!reader.HasRows) return 0;

                        return reader.GetInt32(reader.GetOrdinal("id"));
                    }
                }
                return 0;
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.InnerException}", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }

        }
        private async Task<UserGraphQLResult?> QueryStartggUserData(string userSlug)
        {
            var query = @"query UserQuery($userSlug: String) { 
                            user(slug: $userSlug) { 
                                id name slug player { id gamerTag }}}";

            var request = new GraphQLHttpRequest
            {
                Query = query,
                Variables = new { userSlug }
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
                    var userData = JsonConvert.DeserializeObject<UserGraphQLResult>(tempJson, jsonSerializerSettings);
                    success = true;
                    return userData;
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
        private async Task<UserPlayerData> QueryUserDataByUserLink(int userLink)
        {
            var result = new UserPlayerData
            {
                PlayerId = 0,
                PlayerEmail = "",
                PlayerName = "",
                userLink = 0,
                GameIds = []
            };
            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                const string sql = @"SELECT users.user_name, users.email, users.user_link, p.id, p.player_name FROM users 
                                        JOIN players p ON users.player_id = p.id 
                                        WHERE users.user_link = @UserLink;";

                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@UserLink", userLink);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        if (!reader.HasRows) return result;

                        result.PlayerId = reader.GetInt32(reader.GetOrdinal("id"));
                        result.PlayerEmail = reader.GetString(reader.GetOrdinal("email"));
                        result.PlayerName = reader.GetString(reader.GetOrdinal("player_name"));
                        result.userLink = reader.GetInt32(reader.GetOrdinal("user_link"));
                    }
                    return result;
                }
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.InnerException}", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
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
                    EventId = reader.IsDBNull(reader.GetOrdinal("event_link")) ? 0 : reader.GetInt32(reader.GetOrdinal("event_link")),
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
        private async Task<PhaseGroupGraphQL> QueryPhaseGroupDataByID(int phaseGroupId, int perPage = 50)
        {
            var tempQuery = @"query PhaseGroupSets($phaseGroupId: ID!, $page: Int!, $perPage: Int!) {
                                  phaseGroup(id: $phaseGroupId) { id, displayIdentifier, 
                                    sets(page: $page, perPage: $perPage, sortType: STANDARD) {
                                      pageInfo { total }
                                        nodes { id, slots { id, entrant { id, name }}}
                                        pageInfo { total totalPages page perPage sortBy filter}}}}";

            var jsonSerializerSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Include,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate
            };

            var allNodes = new List<SetNode>();
            int currentPhaseId = 0;
            string poolIdentifier = string.Empty;
            int currentPage = 1;
            int totalPages = int.MaxValue;

            for (int i = 0; i <= totalPages; i++)
            {
                var request = new GraphQLHttpRequest
                {
                    Query = tempQuery,
                    Variables = new
                    {
                        perPage,
                        phaseGroupId,
                        page = currentPage
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
                        var playerData = JsonConvert.DeserializeObject<PhaseGroupGraphQL>(tempJson, jsonSerializerSettings);

                        if (playerData == null || playerData.PhaseGroup == null)
                        {
                            Console.WriteLine("Failed to retrieve player data");
                            totalPages = 0;
                            break;
                        }

                        if (playerData.PhaseGroup.Sets.Nodes != null)
                        {
                            allNodes.AddRange(playerData.PhaseGroup.Sets.Nodes);
                            Console.WriteLine("Tournament Node Added");
                        }

                        currentPhaseId = playerData.PhaseGroup?.Id ?? 0;
                        poolIdentifier = playerData.PhaseGroup?.DisplayIdentifier ?? string.Empty;

                        // Update pagination info for the next iteration
                        var pageInfo = playerData?.PhaseGroup?.Sets?.PageInfo;
                        if (pageInfo != null)
                        {
                            totalPages = pageInfo?.TotalPages ?? 1;
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
                        throw;
                    }
                }
            }
            var result = new PhaseGroupGraphQL
            {
                PhaseGroup = new PhaseGroup
                {
                    Id = currentPhaseId,
                    DisplayIdentifier = poolIdentifier,
                    Sets = new Sets
                    {
                        Nodes = allNodes
                    }
                }
            };
            return result;
        }
        private async Task<PlayerGraphQLResult> QueryStartggPlayerData(int tournamentLink, int perPage = 80)
        {
            var tempQuery = @"query EventEntrants($perPage: Int!, $pageNum: Int!, $eventId: ID!) {
                    event(id: $eventId) {
                        id
                        name
                        tournament { id, name }
                        slug
                        numEntrants
                        entrants(query: {perPage: $perPage, page: $pageNum filter: {}}) {
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
            int currentEventLinkId = 0;
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
                        eventId = tournamentLink,
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

                        currentEventLinkName = playerData.TournamentLink.EventLink?.Name ?? string.Empty;
                        currentEventLinkId = playerData.TournamentLink.EventLink?.Id ?? 0;
                        currentTournamentLinkName = playerData.TournamentLink.Name ?? string.Empty;
                        currentTournamentLinkSlug = playerData.TournamentLink?.Slug ?? string.Empty;
                        currentTournamentLinkId = playerData.TournamentLink?.Id ?? 0;
                        currentEntrantsNum = playerData.TournamentLink?.NumEntrants ?? 0;

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
                    Id = tournamentLink,
                    Name = currentTournamentLinkName,
                    Slug = currentTournamentLinkSlug,
                    NumEntrants = currentEntrantsNum,
                    Entrants = new CommonEntrantList
                    {
                        Nodes = allNodes
                    },
                    EventLink = new CommonTournament
                    {
                        Id = currentEventLinkId,
                        Name = currentEventLinkName
                    }
                }
            };

            return result;
        }
        private async Task<PlayerGraphQLResult?> QueryStartggEventStandings(int tournamentLink, int perPage = 50)
        {
            var tempQuery = @"query EventEntrants($eventId: ID!, $perPage: Int!) {
                                event(id: $eventId) { id name numEntrants slug tournament {
                                  id name } entrants(query: {perPage: $perPage}) {
                                      nodes { id participants {
                                          id player { id gamerTag }} standing { id placement isFinal }}
                                    pageInfo { total totalPages page perPage sortBy filter }}}}";

            var request = new GraphQLHttpRequest
            {
                Query = tempQuery,
                Variables = new
                {
                    tournamentLink,
                    perPage
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
        private async Task<List<PlayerData>> QueryPlayerDataByTournamentId(int[] tournamentLinks)
        {
            List<PlayerData> playerResult = new List<PlayerData>();
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT * FROM get_players_by_tournament_link(@TournamentLinks)", conn))
                    {
                        cmd.Parameters.Add(_commonDatabaseServices.CreateDBIntArrayType("@TournamentLinks", tournamentLinks));

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
