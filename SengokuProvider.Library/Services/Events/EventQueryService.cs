using GraphQL.Client.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Regions;
using SengokuProvider.Library.Services.Common;
using System.Net;

namespace SengokuProvider.Library.Services.Events
{
    public class EventQueryService : IEventQueryService
    {
        private readonly string _connectionString;
        private readonly IntakeValidator _validator;
        private readonly GraphQLHttpClient _client;
        private readonly RequestThrottler _requestThrottler;
        public EventQueryService(string connectionString, GraphQLHttpClient client, IntakeValidator validator, RequestThrottler requestThrottler)
        {
            _connectionString = connectionString;
            _validator = validator;
            _client = client;
            _requestThrottler = requestThrottler;
        }
        public async Task<List<int>> QueryRelatedRegionsById(int regionId)
        {
            try
            {
                var relatedRegions = new List<int>();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new NpgsqlCommand())
                    {
                        cmd.Connection = conn;
                        cmd.CommandText = @"SELECT r2.id
                            FROM public.regions r1
                            JOIN regions r2 ON r1.province = r2.province
                            WHERE r1.id = @InputRegionId
                              AND r2.id != @InputRegionId;";

                        cmd.Parameters.AddWithValue("@InputRegionId", regionId);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows) { return relatedRegions; }
                            while (await reader.ReadAsync())
                            {
                                relatedRegions.Add(reader.GetInt32(0));
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
                    return new RegionData() { Name = "", Latitude = 0.0, Longitude = 0.0, Province = "" };
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
                                    Id = reader.GetInt32(reader.GetOrdinal("id")),
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
        public async Task<List<RegionData>> GetRegionData(List<int> regionIds)
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
                        var param = new NpgsqlParameter("@Input", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer)
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
                                    Id = reader.GetInt32(reader.GetOrdinal("id")),
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
        public async Task<List<AddressEventResult>> QueryEventsByLocation(GetTournamentsByLocationCommand command, int pageNumber)
        {
            if (command == null || command.RegionId == 0) throw new ArgumentNullException(nameof(command));
            try
            {
                var currentRegions = await QueryRelatedRegionsById(command.RegionId);

                var sortedAddresses = new List<AddressEventResult>();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var regionData = await GetRegionData(currentRegions);
                    if (regionData == null || regionData.Count == 0) return sortedAddresses;
                    var regionIds = new List<int>();
                    var locationReference = regionData.FirstOrDefault(x => x.Id == command.RegionId);

                    foreach (var region in regionData)
                    {
                        regionIds.Add(region.Id);
                    }
                    var priorityQueryString = "";

                    switch (command.Priority)
                    {
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

                        var regionIdsParam = cmd.CreateParameter();
                        regionIdsParam.ParameterName = "RegionIds";
                        regionIdsParam.Value = regionIds.ToArray();
                        regionIdsParam.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer;

                        cmd.Parameters.Add(regionIdsParam);
                        cmd.Parameters.AddWithValue("@ReferenceLatitude", locationReference?.Latitude ?? 0);
                        cmd.Parameters.AddWithValue("@ReferenceLongitude", locationReference?.Longitude ?? 0);
                        cmd.Parameters.AddWithValue("@PerPage", command.PerPage);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                sortedAddresses.Add(new AddressEventResult
                                {
                                    Address = reader.IsDBNull(reader.GetOrdinal("address")) ? string.Empty : reader.GetString(reader.GetOrdinal("address")),
                                    Latitude = reader.IsDBNull(reader.GetOrdinal("latitude")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("latitude")),
                                    Longitude = reader.IsDBNull(reader.GetOrdinal("longitude")) ? 0.0 : reader.GetDouble(reader.GetOrdinal("longitude")),
                                    Distance = reader.IsDBNull(reader.GetOrdinal("distance")) ? 0.0 : Math.Round(reader.GetDouble(reader.GetOrdinal("distance")), 4),
                                    EventName = reader.IsDBNull(reader.GetOrdinal("event_name")) ? string.Empty : reader.GetString(reader.GetOrdinal("event_name")),
                                    EventDescription = reader.IsDBNull(reader.GetOrdinal("event_description")) ? string.Empty : reader.GetString(reader.GetOrdinal("event_description")),
                                    Region = reader.IsDBNull(reader.GetOrdinal("region")) ? 0 : reader.GetInt32(reader.GetOrdinal("region")),
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
                            {tournaments(query: {
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
    }
}
