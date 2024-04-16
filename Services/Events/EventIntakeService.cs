using Dapper;
using GraphQL.Client.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using SengokuProvider.API.Models.Common;
using SengokuProvider.API.Models.Events;
using SengokuProvider.API.Services.Common;
using System.Collections.Concurrent;
using System.Dynamic;
using System.Text;

namespace SengokuProvider.API.Services.Events
{
    internal class EventIntakeService : IEventIntakeService
    {
        private readonly IntakeValidator _validator;
        private readonly GraphQLHttpClient _client;

        private readonly string _connectionString;
        private ConcurrentDictionary<string, int> _addressCache;
        public EventIntakeService(string connectionString, GraphQLHttpClient client, IntakeValidator validator)
        {
            _connectionString = connectionString;
            _validator = validator;
            _client = client;
            _addressCache = new ConcurrentDictionary<string, int>();
        }
        public async Task<Tuple<int, int>> IntakeTournamentData(TournamentIntakeCommand intakeCommand)
        {
            try
            {
                var currentQuery = BuildGraphQLQuery(intakeCommand);
                var newEventData = await QueryStartggTournaments(intakeCommand);

                return await ProcessEventData(newEventData);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Unexpected Error Occurred: ", ex);
            }
        }
        private async Task<int> InsertNewAddressData(List<AddressData> data)
        {
            var totalSuccess = 0;

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var transaction = conn.BeginTransaction())
                    {
                        foreach (var newAddress in data)
                        {
                            var createNewInsertCommand = @"
                                INSERT INTO addresses (address, latitude, longitude) 
                                VALUES (@Address, @Latitude, @Longitude) RETURNING id;";

                            using (var command = new NpgsqlCommand(createNewInsertCommand, conn))
                            {
                                command.Transaction = transaction;
                                command.Parameters.AddWithValue("@Address", newAddress.Address ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@Latitude", newAddress.Latitude ?? (object)DBNull.Value);
                                command.Parameters.AddWithValue("@Longitude", newAddress.Longitude ?? (object)DBNull.Value);

                                var result = await command.ExecuteScalarAsync();
                                if (result != null && int.TryParse(result.ToString(), out int addressId) && addressId > 0)
                                {
                                    _addressCache.TryAdd(newAddress.Address, addressId);
                                    newAddress.Id = addressId;
                                    totalSuccess++;
                                }
                            }
                        }
                        transaction.Commit();
                    }
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
            return totalSuccess;
        }
        private async Task<int> InsertNewEventsData(List<EventData> data)
        {
            var totalSuccess = 0;

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var transaction = conn.BeginTransaction())
                    {
                        foreach (var newEvent in data)
                        {
                            var createNewInsertCommand = @"INSERT INTO events (event_name, event_description, region, address_id, start_time, end_time, link_id) 
                            VALUES (@Event_Name, @Event_Description, @Region, @Address_Id, @Start_Time, @End_Time, @Link_Id)";
                            using (var command = new NpgsqlCommand(createNewInsertCommand, conn))
                            {
                                command.Parameters.AddWithValue("@Event_Name", newEvent.EventName);
                                command.Parameters.AddWithValue(@"Event_Description", newEvent.EventDescription);
                                command.Parameters.AddWithValue(@"Region", newEvent.Region);
                                command.Parameters.AddWithValue("@Address_Id", newEvent.AddressID);
                                command.Parameters.AddWithValue(@"Start_Time", newEvent.StartTime);
                                command.Parameters.AddWithValue(@"End_Time", newEvent.EndTime);
                                command.Parameters.AddWithValue(@"Link_Id", newEvent.LinkID);
                                var result = await command.ExecuteNonQueryAsync();
                                if (result > 0) totalSuccess++;
                            }
                        }
                        transaction.Commit();
                    }
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
            return totalSuccess;
        }
        private async Task<Tuple<int, int>> ProcessEventData(EventGraphQLResult queryData)
        {
            var addressMap = new Dictionary<string, int>(); // Map to store address and its ID
            var addresses = new List<AddressData>();
            var events = new List<EventData>();

            foreach (var node in queryData.Tournaments.Nodes)
            {
                if (!_addressCache.TryGetValue(node.VenueAddress, out int addressId))
                {
                    if (!addressMap.ContainsKey(node.VenueAddress))
                    {
                        addressId = await CheckDuplicateAddress(node.VenueAddress);
                        if (addressId == 0)
                        {
                            var addressData = new AddressData
                            {
                                Address = node.VenueAddress,
                                Latitude = node.Lat,
                                Longitude = node.Lng
                            };
                            addresses.Add(addressData);
                        }
                        addressMap[node.VenueAddress] = addressId; // Cache or re-cache the id
                    }
                }
                else
                {
                    addressMap[node.VenueAddress] = addressId;
                }
            }

            // Insert all new addresses and cache the generated IDs
            int addressSuccesses = await InsertNewAddressData(addresses);

            // Update cache with new IDs
            foreach (var address in addresses)
            {
                _addressCache[address.Address] = address.Id;
                addressMap[address.Address] = address.Id;
            }

            // Process events after all addresses are handled
            foreach (var node in queryData.Tournaments.Nodes)
            {
                if (!await CheckDuplicateEvents(node.Id))
                {
                    var eventData = new EventData
                    {
                        LinkID = node.Id,
                        EventName = node.Name,
                        EventDescription = "Sample description",
                        Region = 1,
                        AddressID = addressMap[node.VenueAddress],  // Use the confirmed address ID from the map
                        StartTime = DateTimeOffset.FromUnixTimeSeconds(node.StartAt).DateTime,
                        EndTime = DateTimeOffset.FromUnixTimeSeconds(node.EndAt).DateTime
                    };
                    events.Add(eventData);
                }
            }

            // Insert events data
            int eventSuccesses = await InsertNewEventsData(events);

            return Tuple.Create(addressSuccesses, eventSuccesses);
        }
        private async Task<int> CheckDuplicateAddress(string address)
        {
            try
            {
                if (_addressCache.TryGetValue(address, out int addressId) && addressId != 0) return addressId;
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    var newQuery = @"SELECT id FROM addresses WHERE address = @Input";
                    addressId = await conn.QueryFirstOrDefaultAsync<int>(newQuery, new { Input = address });

                    if (addressId != 0) _addressCache.TryAdd(address, addressId);

                    return addressId;
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
        private async Task<bool> CheckDuplicateEvents(int linkId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    var newQuery = @"SELECT id FROM events WHERE link_id = @Input";
                    var result = await conn.QueryFirstOrDefaultAsync<int>(newQuery, new { Input = linkId });

                    if (result != 0) return true;
                }
                return false;
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
        private async Task<EventGraphQLResult> QueryStartggTournaments(TournamentIntakeCommand command)
        {
            var tempQuery = @"query TournamentsByState($perPage: Int, $state: String!, $yearStart: Timestamp, $yearEnd: Timestamp) {tournaments(query: {perPage: $perPage,filter: {addrState: $state,afterDate: $yearStart,beforeDate: $yearEnd}}) {nodes {id,name,addrState,lat,lng,venueAddress,startAt,endAt}}}";


            var request = new GraphQLHttpRequest
            {
                Query = tempQuery,
                Variables = new
                {
                    perPage = command.Page,
                    state = command.StateCode,
                    yearStart = command.StartDate,
                    yearEnd = command.EndDate,
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
        private string BuildGraphQLQuery(TournamentIntakeCommand command)
        {
            if (!ValidateFilters(command.Filters))
                throw new ArgumentException("Invalid filters provided");

            var queryBuilder = new StringBuilder();
            queryBuilder.Append("query TournamentsByState(");

            // Append variables with correct formatting
            for (int i = 0; i < command.Variables.Length; i++)
            {
                queryBuilder.Append($"{command.Variables[i]}");
                if (i < command.Variables.Length - 1)
                    queryBuilder.Append(", ");
            }

            queryBuilder.Append(") { tournaments(query: {");
            queryBuilder.Append($"perPage: {command.Page} ");
            queryBuilder.Append("filter: {");

            // Predefined start and end dates
            queryBuilder.Append($"afterDate: {command.StartDate}, ");
            queryBuilder.Append($"beforeDate: {command.EndDate}, ");
            queryBuilder.Append($"addrState: {command.StateCode}, ");
            // Dynamically add additional filters if present
            for (int i = 0; i < command.Filters.Length; i++)
            {
                queryBuilder.Append($"{command.Filters[i]}");
                if (i < command.Filters.Length - 1)
                    queryBuilder.Append(", "); // Append comma only if it's not the last filter
            }
            queryBuilder.Append("}}) { nodes {");
            queryBuilder.Append("id name addrState lat lng venueAddress startAt endAt");
            queryBuilder.Append("}}}");

            return queryBuilder.ToString();
        }
        private bool ValidateFilters(string[] filters)
        {
            if (filters.Length == 0) return true;
            var allowedFields = new HashSet<string>
            {
                "id", "ids", "ownerId", "isCurrentUserAdmin", "countryCode", "addrState",
                "location", "afterDate", "beforeDate", "computedUpdatedAt", "name", "venueName",
                "isFeatured", "isLeague", "hasBannerImages", "activeShops", "regOpen", "past",
                "published", "publiclySearchable", "staffPicks", "hasOnlineEvents", "topGames",
                "upcoming", "videogameIds", "sortByScore"
            };

            foreach (var filter in filters)
            {
                var parts = filter.Split(new[] { ':' }, 2);
                if (parts.Length != 2 || !allowedFields.Contains(parts[0].Trim()))
                {
                    return false;
                }
            }

            return true;
        }
        private object ParseVariables(string[] variableDeclarations)
        {
            var variableDictionary = new ExpandoObject() as IDictionary<string, Object>;

            foreach (var declaration in variableDeclarations)
            {
                var parts = declaration.Split(':');
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim().Trim('\''); // Remove any single quotes around string values

                    if (int.TryParse(value, out int intValue)) //Assumes that all values are strings/int
                    {
                        variableDictionary[key] = intValue;
                    }
                    else
                    {
                        variableDictionary[key] = value;
                    }
                }
            }
            return variableDictionary;
        }
    }
}
