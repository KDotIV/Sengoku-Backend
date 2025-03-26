using GraphQL.Client.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using NpgsqlTypes;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Regions;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using System.Net;

namespace SengokuProvider.Library.Services.Events
{
    public class EventQueryService : IEventQueryService
    {
        private readonly string _connectionString;
        private readonly ICommonDatabaseService _commonServices;
        private readonly IntakeValidator _validator;
        private readonly GraphQLHttpClient _client;
        private readonly RequestThrottler _requestThrottler;
        public EventQueryService(string connectionString, GraphQLHttpClient client, IntakeValidator validator, RequestThrottler requestThrottler, ICommonDatabaseService commonServices)
        {
            _connectionString = connectionString;
            _validator = validator;
            _client = client;
            _requestThrottler = requestThrottler;
            _commonServices = commonServices;
        }
        public async Task<List<string>> QueryRelatedRegionsById(string regionId)
        {
            try
            {
                var relatedRegions = new List<string>();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new NpgsqlCommand())
                    {
                        cmd.Connection = conn;
                        cmd.CommandText = @"SELECT DISTINCT r2.id FROM public.regions r1 JOIN regions r2 ON r1.province = r2.province WHERE r1.id LIKE @InputRegionId";

                        cmd.Parameters.AddWithValue("@InputRegionId", regionId + "%");

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows) { return relatedRegions; }
                            while (await reader.ReadAsync())
                            {
                                relatedRegions.Add(reader.GetString(reader.GetOrdinal("id")));
                            }
                        }
                    }
                }
                relatedRegions.Add(regionId);
                return relatedRegions;
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
        public async Task<List<string>> QueryLocalRegionsById(string regionId)
        {
            try
            {
                var relatedRegions = new List<string>();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new NpgsqlCommand())
                    {
                        cmd.Connection = conn;
                        cmd.CommandText = @"SELECT DISTINCT id FROM public.regions WHERE id LIKE @InputRegionId";

                        cmd.Parameters.AddWithValue("@InputRegionId", regionId + "%");

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows) { return relatedRegions; }
                            while (await reader.ReadAsync())
                            {
                                relatedRegions.Add(reader.GetString(reader.GetOrdinal("id")));
                            }
                        }
                    }
                }
                relatedRegions.Add(regionId);
                return relatedRegions;
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
        public async Task<RegionData?> QueryRegion(GetRegionCommand command)
        {
            try
            {
                if (!command.Validate())
                {
                    Console.WriteLine($"Parameters cannot be null: {command.QueryParameter.Item1} - Value: {command.QueryParameter.Item2}");
                    return new RegionData() { Id = "", Name = "", Latitude = 0.0, Longitude = 0.0, Province = "" };
                }
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    // Ensure the column name is safe to use
                    var columnName = command.QueryParameter.Item1;

                    // Construct the SQL query dynamically
                    var sql = $"SELECT * FROM regions WHERE \"{columnName}\" = @Value;";

                    using (var cmd = new NpgsqlCommand(sql, conn))
                    {
                        // Add parameter for the value
                        var parameter = new NpgsqlParameter("@Value", command.QueryParameter.Item2);

                        if (int.TryParse(command.QueryParameter.Item2, out int intValue))
                        {
                            parameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer;
                            parameter.Value = intValue;
                        }
                        else if (DateTime.TryParse(command.QueryParameter.Item2, out DateTime dateValue))
                        {
                            parameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Timestamp;
                            parameter.Value = dateValue;
                        }
                        else if (bool.TryParse(command.QueryParameter.Item2, out bool boolValue))
                        {
                            parameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean;
                            parameter.Value = boolValue;
                        }
                        else
                        {
                            parameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Varchar;
                            parameter.Value = command.QueryParameter.Item2;
                        }

                        cmd.Parameters.Add(parameter);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                return new RegionData
                                {
                                    Id = reader.GetString(reader.GetOrdinal("id")),
                                    Name = reader.GetString(reader.GetOrdinal("name")),
                                    Latitude = reader.GetDouble(reader.GetOrdinal("latitude")),
                                    Longitude = reader.GetDouble(reader.GetOrdinal("longitude")),
                                    Province = reader.GetString(reader.GetOrdinal("province"))
                                };
                            }
                        }
                    }
                }
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error While Processing: {ex.Message} - {ex.StackTrace}");
            }
            return null;
        }
        public async Task<List<RegionData>> GetRegionData(List<string> regionIds)
        {
            var regions = new List<RegionData>();
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT id, name, latitude, longitude, province FROM regions WHERE id = ANY(@Input)", conn))
                    {
                        // Passing the list as an array parameter
                        var param = new NpgsqlParameter("@Input", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text)
                        {
                            Value = regionIds.ToArray()
                        };
                        cmd.Parameters.Add(param);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                regions.Add(new RegionData
                                {
                                    Id = reader.GetString(reader.GetOrdinal("id")),
                                    Name = reader.GetString(reader.GetOrdinal("name")),
                                    Latitude = reader.GetDouble(reader.GetOrdinal("latitude")),
                                    Longitude = reader.GetDouble(reader.GetOrdinal("longitude")),
                                    Province = reader.GetString(reader.GetOrdinal("province"))
                                });
                            }
                        }
                    }

                    return regions;
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
        }
        public async Task<AddressData> GetAddressById(int addressId)
        {
            try
            {
                var addressData = new AddressData();
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"Select * From addresses WHERE id = @Input", conn))
                    {
                        cmd.Parameters.AddWithValue("@Input", addressId);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                addressData.Address = reader.GetString(reader.GetOrdinal("address"));
                                addressData.Longitude = reader.GetDouble(reader.GetOrdinal("longitude"));
                                addressData.Latitude = reader.GetDouble(reader.GetOrdinal("latitude"));
                            }
                        }
                    }
                }
                return addressData;
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
        public async Task<List<TournamentData>> GetTournamentLinksById(int[] tournamentLinkId)
        {
            try
            {
                var tournamentResults = new List<TournamentData>();
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"Select * From tournament_links WHERE id = ANY(@Input)", conn))
                    {
                        cmd.Parameters.AddWithValue("@Input", tournamentLinkId);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var newTournamentLink = new TournamentData { Id = 0, UrlSlug = string.Empty, EventId = 0, LastUpdated = DateTime.MinValue };
                                newTournamentLink.Id = reader.GetInt32(reader.GetOrdinal("id"));
                                newTournamentLink.UrlSlug = reader.GetString(reader.GetOrdinal("url_slug"));
                                newTournamentLink.GameId = reader.GetInt32(reader.GetOrdinal("game_id"));
                                newTournamentLink.EventId = reader.GetInt32(reader.GetOrdinal("event_link"));
                                newTournamentLink.EntrantsNum = reader.GetInt32(reader.GetOrdinal("entrants_num"));
                                newTournamentLink.LastUpdated = reader.GetDateTime(reader.GetOrdinal("last_updated"));
                                tournamentResults.Add(newTournamentLink);
                            }
                        }
                    }
                }
                return tournamentResults;
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
        public async Task<List<TournamentData>> GetTournamentsByEventIds(int[] eventIds)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                using (var cmd = new NpgsqlCommand(@"Select * FROM tournament_links WHERE event_link = ANY(@Input)", connection))
                {
                    cmd.Parameters.AddWithValue("@Input", eventIds);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (!reader.HasRows) return new List<TournamentData>();
                        else
                        {
                            var tournamentResults = new List<TournamentData>();
                            while (await reader.ReadAsync())
                            {
                                var newTournamentLink = new TournamentData { Id = 0, UrlSlug = string.Empty, EventId = 0, LastUpdated = DateTime.MinValue };
                                newTournamentLink.Id = reader.GetInt32(reader.GetOrdinal("id"));
                                newTournamentLink.UrlSlug = reader.GetString(reader.GetOrdinal("url_slug"));
                                newTournamentLink.GameId = reader.GetInt32(reader.GetOrdinal("game_id"));
                                newTournamentLink.EventId = reader.GetInt32(reader.GetOrdinal("event_link"));
                                newTournamentLink.EntrantsNum = reader.GetInt32(reader.GetOrdinal("entrants_num"));
                                newTournamentLink.LastUpdated = reader.GetDateTime(reader.GetOrdinal("last_updated"));
                                tournamentResults.Add(newTournamentLink);
                            }
                            return tournamentResults;
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
        public async Task<List<AddressEventResult>> GetEventsByLocation(GetTournamentsByLocationCommand command, int pageNumber)
        {
            if (command == null || string.IsNullOrEmpty(command.RegionId)) throw new ArgumentNullException(nameof(command));
            if (command.GameIds.Length == 0) command.GameIds = [.. StartggGameIds.GameIds];

            try
            {
                var tempParts = StringExtensions.SplitByNum(command.RegionId, 2);
                var splitZipcode = tempParts.First();
                List<string> currentRegions = new List<string>();

                switch (command.Priority)
                {
                    case "local":
                        currentRegions = await QueryLocalRegionsById(splitZipcode);
                        break;
                    default:
                        currentRegions = await QueryRelatedRegionsById(splitZipcode);
                        break;
                }
                var sortedAddresses = new List<AddressEventResult>();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var regionData = await GetRegionData(currentRegions);
                    if (regionData == null || regionData.Count == 0) return sortedAddresses;
                    var regionIds = new List<string>();
                    var locationReference = regionData.FirstOrDefault(x => x.Id == command.RegionId);

                    foreach (var region in regionData)
                    {
                        regionIds.Add(region.Id);
                    }
                    var priorityQueryString = "";

                    switch (command.Priority)
                    {
                        case "local":
                            priorityQueryString = QueryConstants.LocalPriority;
                            break;
                        case "national":
                            priorityQueryString = QueryConstants.NationalPriority;
                            break;
                        case "distance":
                            priorityQueryString = QueryConstants.DistancePriority;
                            break;
                        case "date":
                            priorityQueryString = QueryConstants.DatePriority;
                            break;
                        default:
                            priorityQueryString = QueryConstants.DatePriority;
                            break;
                    }

                    using (var cmd = new NpgsqlCommand())
                    {
                        cmd.Connection = conn;
                        cmd.CommandText = priorityQueryString;
                        cmd.Parameters.Add(_commonServices.CreateDBTextArrayType("@RegionIds", regionIds.ToArray()));
                        cmd.Parameters.Add(_commonServices.CreateDBIntArrayType("@GameIds", command.GameIds));
                        cmd.Parameters.AddWithValue("@ReferenceLatitude", locationReference?.Latitude ?? 0);
                        cmd.Parameters.AddWithValue("@ReferenceLongitude", locationReference?.Longitude ?? 0);
                        cmd.Parameters.AddWithValue("@PerPage", command.PerPage);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                sortedAddresses.Add(new AddressEventResult
                                {
                                    EventId = reader.IsDBNull(reader.GetOrdinal("id")) ? 0 : reader.GetInt32(reader.GetOrdinal("id")),
                                    Address = reader.IsDBNull(reader.GetOrdinal("address")) ? string.Empty : reader.GetString(reader.GetOrdinal("address")),
                                    Latitude = reader.IsDBNull(reader.GetOrdinal("latitude")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("latitude")),
                                    Longitude = reader.IsDBNull(reader.GetOrdinal("longitude")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("longitude")),
                                    Distance = reader.IsDBNull(reader.GetOrdinal("distance")) ? 0.0 : Math.Round(reader.GetDouble(reader.GetOrdinal("distance")), 4),
                                    EventName = reader.IsDBNull(reader.GetOrdinal("event_name")) ? string.Empty : reader.GetString(reader.GetOrdinal("event_name")),
                                    EventDescription = reader.IsDBNull(reader.GetOrdinal("event_description")) ? string.Empty : reader.GetString(reader.GetOrdinal("event_description")),
                                    Region = reader.IsDBNull(reader.GetOrdinal("region")) ? string.Empty : reader.GetString(reader.GetOrdinal("region")),
                                    StartTime = reader.IsDBNull(reader.GetOrdinal("start_time")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("start_time")),
                                    EndTime = reader.IsDBNull(reader.GetOrdinal("end_time")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("end_time")),
                                    LinkId = reader.IsDBNull(reader.GetOrdinal("link_id")) ? 0 : reader.GetInt32(reader.GetOrdinal("link_id")),
                                    ClosingRegistration = reader.IsDBNull(reader.GetOrdinal("closing_registration_date")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("closing_registration_date")),
                                    UrlSlug = reader.IsDBNull(reader.GetOrdinal("url_slug")) ? string.Empty : reader.GetString(reader.GetOrdinal("url_slug")),
                                    IsOnline = reader.IsDBNull(reader.GetOrdinal("online_tournament")) ? false : reader.GetBoolean(reader.GetOrdinal("online_tournament"))
                                });
                            }
                        }
                    }
                }
                return sortedAddresses;
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
        public async Task<EventGraphQLResult?> QueryStartggEventByEventId(int eventId)
        {
            var tempQuery = @"query TournamentQuery($tournamentId: ID!) 
                            { tournaments(query: {
                            filter: {
                                id: $tournamentId
                                    }}) {
                                    nodes {id,name,addrState,lat,lng,registrationClosesAt,isRegistrationOpen,venueAddress,startAt,endAt,slug}}}";

            var request = new GraphQLHttpRequest
            {
                Query = tempQuery,
                Variables = new
                {
                    tournamentId = eventId,
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
                    if (response.Data == null) throw new Exception("Failed to retrieve tournament data.");

                    var tempJson = JsonConvert.SerializeObject(response.Data, Formatting.Indented);
                    var eventData = JsonConvert.DeserializeObject<EventGraphQLResult>(tempJson);

                    success = true;
                    return eventData;
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
            return null;
        }
        public async Task<List<TournamentData>> GetTournamentLinksByUrl(string eventLinkSlug, int[]? gameIds = default)
        {
            if (string.IsNullOrEmpty(eventLinkSlug)) return new List<TournamentData>();

            return await VerifyEventLinkExists(eventLinkSlug);
        }
        public async Task<TournamentsBySlugGraphQLResult?> QueryStartggTournamentLinksByUrl(string eventLinkSlug)
        {
            var tempQuery = @"query TournamentEvents($tourneySlug: String!) { tournament(slug: $tourneySlug) {
                                    id, name, events { id, name }}}";

            var request = new GraphQLHttpRequest
            {
                Query = tempQuery,
                Variables = new
                {
                    tourneySlug = eventLinkSlug,
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
                    if (response.Data == null) throw new Exception("Failed to retrieve tournament data.");

                    var tempJson = JsonConvert.SerializeObject(response.Data, Formatting.Indented);
                    var eventData = JsonConvert.DeserializeObject<TournamentsBySlugGraphQLResult>(tempJson);

                    success = true;
                    return eventData;
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
            return null;
        }
        public async Task<List<AddressEventResult>> GetLocalEventsByLeagueRegions(int[] leagueIds, string[] regions, int page)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"SELECT * FROM get_nearby_events_for_leagues(@LeagueIds, @RegionIds, @PerPage)";
                await using var sqlcmd = new NpgsqlCommand(sql, connection);
                Console.WriteLine("RegionIds Type: " + regions.GetType().Name);
                Console.WriteLine("RegionIds Count: " + regions.Length);

                var cleanedRegions = _commonServices.SanitizeStringArray(regions);

                sqlcmd.Parameters.AddWithValue("LeagueIds", NpgsqlDbType.Array | NpgsqlDbType.Integer, leagueIds);
                sqlcmd.Parameters.AddWithValue("RegionIds", NpgsqlDbType.Array | NpgsqlDbType.Varchar, cleanedRegions);
                sqlcmd.Parameters.AddWithValue("PerPage", NpgsqlDbType.Integer, page);

                using (var reader = await sqlcmd.ExecuteReaderAsync())
                {
                    var results = new List<AddressEventResult>();
                    while (await reader.ReadAsync())
                    {
                        results.Add(
                            new AddressEventResult
                            {
                                EventId = reader.IsDBNull(reader.GetOrdinal("event_id")) ? 0 : reader.GetInt32(reader.GetOrdinal("event_id")),
                                Address = reader.IsDBNull(reader.GetOrdinal("address")) ? string.Empty : reader.GetString(reader.GetOrdinal("address")),
                                Latitude = reader.IsDBNull(reader.GetOrdinal("latitude")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("latitude")),
                                Longitude = reader.IsDBNull(reader.GetOrdinal("longitude")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("longitude")),
                                Distance = reader.IsDBNull(reader.GetOrdinal("distance")) ? 0.0 : Math.Round(reader.GetDouble(reader.GetOrdinal("distance")), 4),
                                EventName = reader.IsDBNull(reader.GetOrdinal("event_name")) ? string.Empty : reader.GetString(reader.GetOrdinal("event_name")),
                                EventDescription = reader.IsDBNull(reader.GetOrdinal("event_description")) ? string.Empty : reader.GetString(reader.GetOrdinal("event_description")),
                                Region = reader.IsDBNull(reader.GetOrdinal("region")) ? string.Empty : reader.GetString(reader.GetOrdinal("region")),
                                StartTime = reader.IsDBNull(reader.GetOrdinal("start_time")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("start_time")),
                                EndTime = reader.IsDBNull(reader.GetOrdinal("end_time")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("end_time")),
                                LinkId = reader.IsDBNull(reader.GetOrdinal("link_id")) ? 0 : reader.GetInt32(reader.GetOrdinal("link_id")),
                                ClosingRegistration = reader.IsDBNull(reader.GetOrdinal("closing_registration_date")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("closing_registration_date")),
                                UrlSlug = reader.IsDBNull(reader.GetOrdinal("url_slug")) ? string.Empty : reader.GetString(reader.GetOrdinal("url_slug")),
                                IsOnline = reader.IsDBNull(reader.GetOrdinal("online_tournament")) ? false : reader.GetBoolean(reader.GetOrdinal("online_tournament"))
                            });
                    }
                    return results;
                }
            }
            catch (NpgsqlException ex)
            {
                Console.WriteLine($"NpgsqlException: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                throw;
            }
        }
        public async Task<List<TournamentData>> GetTournamentsByLeagueIds(int[] leagueIds)
        {
            try
            {
                var result = new List<TournamentData>();
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var sql = @"select tl.id, tl.url_slug, tl.game_id, tl.event_link, tl.entrants_num, tl.last_updated 
                            FROM tournament_leagues l 
                            JOIN tournament_links tl ON l.tournament_id = tl.id 
                            WHERE league_id = ANY(@LeagueIds)";
                await using var sqlcmd = new NpgsqlCommand(sql, connection);

                sqlcmd.Parameters.AddWithValue("LeagueIds", leagueIds);
                using (var reader = await sqlcmd.ExecuteReaderAsync())
                {
                    if (!reader.HasRows)
                    {
                        Console.WriteLine("No tournaments were found with given LeagueIds");
                        return result;
                    }
                    while (await reader.ReadAsync())
                    {
                        result.Add(new TournamentData
                        {
                            Id = reader.GetInt32(0),
                            UrlSlug = reader.GetString(1),
                            GameId = reader.GetInt32(2),
                            EventId = reader.GetInt32(3),
                            EntrantsNum = reader.GetInt32(4),
                            LastUpdated = reader.GetDateTime(5)
                        });
                    }
                    return result;
                }
            }
            catch (NpgsqlException ex)
            {
                Console.WriteLine($"NpgsqlException: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                throw;
            }
        }
        private async Task<List<TournamentData>> VerifyEventLinkExists(string eventLinkSlug, int[]? gameIds = null)
        {
            try
            {
                var result = new List<TournamentData>();
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new NpgsqlCommand("SELECT * FROM get_tournament_links_by_url(@urlTextInput, @GameArrInput)", conn))
                    {
                        cmd.Parameters.AddWithValue("@urlTextInput", eventLinkSlug);
                        cmd.Parameters.AddWithValue("@GameArrInput", gameIds ?? (object)DBNull.Value);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows)
                            {
                                Console.WriteLine("No tournaments were found with that Url");
                                return result;
                            }
                            while (await reader.ReadAsync())
                            {
                                result.Add(new TournamentData
                                {
                                    Id = reader.GetInt32(0),
                                    UrlSlug = reader.GetString(1),
                                    GameId = reader.GetInt32(2),
                                    EventId = reader.GetInt32(3),
                                    EntrantsNum = reader.GetInt32(4),
                                    LastUpdated = reader.GetDateTime(5)
                                });
                            }
                            return result;
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
    }
}