using GraphQL.Client.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Regions;
using SengokuProvider.Library.Services.Common;

namespace SengokuProvider.Library.Services.Events
{
    public class EventQueryService : IEventQueryService
    {
        private readonly string _connectionString;
        private readonly IntakeValidator _validator;
        private readonly GraphQLHttpClient _client;
        public EventQueryService(string connectionString, GraphQLHttpClient client, IntakeValidator validator)
        {
            _connectionString = connectionString;
            _validator = validator;
            _client = client;
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
            if (!command.Validate())
            {
                Console.WriteLine("Invalid command parameters:");
                Console.WriteLine($"Param Name: {command.QueryParameter.Item1}");
                Console.WriteLine($"Param value: {command.QueryParameter.Item2}");
                throw new ArgumentException("Invalid command parameters");
            }

            try
            {
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
                        cmd.Parameters.AddWithValue("@ReferenceLatitude", locationReference.Latitude);
                        cmd.Parameters.AddWithValue("@ReferenceLongitude", locationReference.Longitude);
                        cmd.Parameters.AddWithValue("@PerPage", command.PerPage);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                sortedAddresses.Add(new AddressEventResult
                                {
                                    Address = reader.GetString(reader.GetOrdinal("address")),
                                    Latitude = reader.GetDouble(reader.GetOrdinal("latitude")),
                                    Longitude = reader.GetDouble(reader.GetOrdinal("longitude")),
                                    Distance = Math.Round(reader.GetDouble(reader.GetOrdinal("distance")), 4),
                                    EventName = reader.GetString(reader.GetOrdinal("event_name")),
                                    EventDescription = reader.GetString(reader.GetOrdinal("event_description")),
                                    Region = reader.GetInt32(reader.GetOrdinal("region")),
                                    StartTime = reader.GetDateTime(reader.GetOrdinal("start_time")),
                                    EndTime = reader.GetDateTime(reader.GetOrdinal("end_time")),
                                    LinkId = reader.GetInt32(reader.GetOrdinal("link_id")),
                                    ClosingRegistration = reader.GetDateTime(reader.GetOrdinal("closing_registration_date"))
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
        public async Task<PlayerStandingResult> QueryPlayerStandings(GetPlayerStandingsCommand command)
        {
            try
            {
                var data = await QueryStartggStandings(command);
                if (data == null) { throw new NullReferenceException(); }

                var newStandingResult = MapStandingsData(data);

                return newStandingResult;
            }
            catch (Exception ex)
            {
                return new PlayerStandingResult { Response = $"Failed: {ex.Message} - {ex.StackTrace}" };
                throw;
            }
        }
        private PlayerStandingResult MapStandingsData(StandingGraphQLResult data)
        {
            var tempNode = data.Event.Entrants.Nodes.FirstOrDefault();
            var mappedResult = new PlayerStandingResult
            {
                Response = "Open",
                Standing = tempNode.Standing.Placement,
                GamerTag = tempNode.Participants.FirstOrDefault().GamerTag,
                EntrantsNum = tempNode.Standing.Container.NumEntrants,
                EventDetails = new EventDetails
                {
                    EventId = tempNode.Standing.Container.Tournament.Id,
                    EventName = tempNode.Standing.Container.Tournament.Name,
                    TournamentId = tempNode.Standing.Container.Id,
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
        public async Task<EventGraphQLResult?> QueryStartggEventByEventId(int eventId)
        {
            var tempQuery = @"query TournamentQuery($tournamentId: ID!) 
                {tournaments(query: {
                    filter: {
                        id: $tournamentId
                            }}) {
                            nodes {
                                id,name,addrState,lat,lng,registrationClosesAt,isRegistrationOpen,venueAddress,startAt,endAt}}}";

            var request = new GraphQLHttpRequest
            {
                Query = tempQuery,
                Variables = new
                {
                    tournamentId = eventId,
                }
            };
            try
            {
                var response = await _client.SendQueryAsync<JObject>(request);
                if (response.Data == null) throw new Exception($"Failed to retrieve tournament data. ");

                var tempJson = JsonConvert.SerializeObject(response.Data, Formatting.Indented);

                var eventData = JsonConvert.DeserializeObject<EventGraphQLResult>(tempJson);

                return eventData;
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
