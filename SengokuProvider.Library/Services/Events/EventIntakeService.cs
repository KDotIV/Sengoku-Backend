using Dapper;
using GraphQL.Client.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Services.Common;
using System.Collections.Concurrent;

namespace SengokuProvider.Library.Services.Events
{
    public class EventIntakeService : IEventIntakeService
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
        public async Task<Tuple<int, int>> IntakeTournamentData(IntakeEventsByLocationCommand intakeCommand)
        {
            try
            {
                EventGraphQLResult newEventData = await QueryStartggTournamentsByState(intakeCommand);

                return await ProcessEventData(newEventData);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        public async Task<int> IntakeEventsByGameId(IntakeEventsByGameIdCommand intakeCommand)
        {
            try
            {
                EventGraphQLResult newEventData = await QueryStartggEventsByGameId(intakeCommand);
                return await ProcessGameData(newEventData);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        public async Task<bool> IntakeEventsByTournamentId(int tournamentId)
        {
            try
            {
                EventGraphQLResult newEventData = await QueryStartggEventByTournamentId(tournamentId);

                var eventResult = await ProcessEventData(newEventData);

                if (eventResult == null) { Console.WriteLine("No Result from Processing Event Records. Either an Error Occured or this Record was already inserted"); return false; }

                var gameResult = await ProcessGameData(newEventData);

                if (gameResult == 0) { Console.WriteLine("No Result from Processing Game Records. Either an Error Occured or this Record was already inserted"); return false; }

                return true;
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        private async Task<int> ProcessGameData(EventGraphQLResult newEventData)
        {
            var totalSuccess = 0;
            try
            {
                var currentBatch = await BuildTournamentData(newEventData);
                totalSuccess = await InsertNewTournamentData(totalSuccess, currentBatch);

                return totalSuccess;
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.StackTrace}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error While Processing: {ex.Message} - {ex.StackTrace}");
            }
            return totalSuccess;
        }

        private async Task<int> InsertNewTournamentData(int totalSuccess, List<TournamentData> currentBatch)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var transaction = await conn.BeginTransactionAsync())
                {
                    foreach (var tournament in currentBatch)
                    {
                        var createInsertCommand = @"
                            INSERT INTO tournament_links (id, url_slug, games_ids)
                            VALUES (@Input, @UrlSlug, @Games)
                            ON CONFLICT (id) DO UPDATE SET
                                url_slug = EXCLUDED.url_slug,
                                games_ids = EXCLUDED.games_ids;";
                        using (var command = new NpgsqlCommand(createInsertCommand, conn))
                        {
                            command.Transaction = transaction;
                            command.Parameters.AddWithValue(@"Input", tournament.Id);
                            command.Parameters.AddWithValue(@"UrlSlug", tournament.UrlSlug);

                            var gamesParam = CreateDBIntArrayType("Games", tournament.Games);
                            command.Parameters.Add(gamesParam);

                            var result = await command.ExecuteNonQueryAsync();
                            if (result > 0) totalSuccess += result;
                        }
                    }
                    await transaction.CommitAsync();
                }
            }

            return totalSuccess;
        }

        private async Task<List<TournamentData>> BuildTournamentData(EventGraphQLResult newEventData)
        {
            var tournamentBatch = new List<TournamentData>();

            foreach (var tournament in newEventData.Tournaments.Nodes)
            {
                TournamentData newTournament = new TournamentData
                {
                    Id = tournament.Id,
                    UrlSlug = tournament.Slug,
                    Games = tournament.Events.Select(x => x.Videogame.Id).ToArray()
                };

                if (!await VerifyTournamentLink(newTournament.Id))
                {
                    await IntakeEventsByTournamentId(tournament.Id);
                    throw new Exception($"ID: {tournament.Id} does not exist in database. Attempting to Find Events/Tournaments");
                }

                tournamentBatch.Add(newTournament);
            }
            return tournamentBatch;
        }

        private async Task<bool> VerifyTournamentLink(int id)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var newQuery = @"SELECT link_id FROM events 
                        WHERE link_id = @Input AND link_id IN (SELECT id FROM tournament_links);";
                    var result = await conn.QueryFirstOrDefaultAsync<int>(newQuery, new { Input = id });

                    if (result <= 0) return false;
                }
                return true;
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.StackTrace}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error While Processing: {ex.Message} - {ex.StackTrace}");
            }
            return false;
        }
        private async Task<int> InsertNewAddressData(List<AddressData> data)
        {
            var totalSuccess = 0;

            if (data == null || data.Count == 0) return 0;

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
                throw new ApplicationException($"Database error occurred: {ex.StackTrace}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error While Processing: {ex.Message} - {ex.StackTrace}");
            }
            return totalSuccess;
        }
        private async Task<int> InsertNewEventsData(List<EventData> data)
        {
            var totalSuccess = 0;

            if (data == null || data.Count == 0) return 0;

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var transaction = conn.BeginTransaction())
                    {
                        foreach (var newEvent in data)
                        {
                            await EnsureLinkIdExists(newEvent.LinkID, conn);

                            var createNewInsertCommand = @"INSERT INTO events (event_name, event_description, region, address_id, start_time, end_time, link_id, closing_registration_date,registration_open) 
                            VALUES (@Event_Name, @Event_Description, @Region, @Address_Id, @Start_Time, @End_Time, @Link_Id, @ClosingRegistration, @IsRegistrationOpen)
                            ON CONFLICT (link_id) DO UPDATE SET
                                event_name = EXCLUDED.event_name,
                                event_description = EXCLUDED.event_description,
                                region = EXCLUDED.region,
                                address_id = EXCLUDED.address_id,
                                start_time = EXCLUDED.start_time,
                                end_time = EXCLUDED.end_time,
                                closing_registration_date = EXCLUDED.closing_registration_date,
                                registration_open = EXCLUDED.registration_open;";
                            using (var command = new NpgsqlCommand(createNewInsertCommand, conn))
                            {
                                command.Parameters.AddWithValue("@Event_Name", newEvent.EventName);
                                command.Parameters.AddWithValue(@"Event_Description", newEvent.EventDescription);
                                command.Parameters.AddWithValue(@"Region", newEvent.Region);
                                command.Parameters.AddWithValue("@Address_Id", newEvent.AddressID);
                                command.Parameters.AddWithValue(@"Start_Time", newEvent.StartTime);
                                command.Parameters.AddWithValue(@"End_Time", newEvent.EndTime);
                                command.Parameters.AddWithValue(@"Link_Id", newEvent.LinkID);
                                command.Parameters.AddWithValue(@"ClosingRegistration", newEvent.ClosingRegistration);
                                command.Parameters.AddWithValue(@"IsRegistrationOpen", newEvent.IsRegistrationOpen);
                                var result = await command.ExecuteNonQueryAsync();
                                if (result > 0) totalSuccess++;
                            }
                        }
                        await transaction.CommitAsync();
                    }
                }
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.StackTrace}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error While Processing: {ex.Message} - {ex.StackTrace}");
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
                        EndTime = DateTimeOffset.FromUnixTimeSeconds(node.EndAt).DateTime,
                        ClosingRegistration = DateTimeOffset.FromUnixTimeSeconds(node.RegistrationClosesAt).DateTime,
                        IsRegistrationOpen = node.IsRegistrationOpen
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
                throw new ApplicationException($"Database error occurred: {ex.StackTrace}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error While Processing: {ex.Message} - {ex.StackTrace}");
            }
            return 0;
        }
        private async Task EnsureLinkIdExists(int linkId, NpgsqlConnection conn)
        {
            var cmdText = @"
                SELECT EXISTS (
                    SELECT 1 FROM tournament_links WHERE id = @linkId
                );";
            try
            {
                using (var cmd = new NpgsqlCommand(cmdText, conn))
                {
                    cmd.Parameters.AddWithValue("@linkId", linkId);
                    var result = await cmd.ExecuteScalarAsync();
                    bool exists = result != null && (bool)result;

                    if (!exists)
                    {
                        // Insert into tournament_links if not exists
                        var insertCmdText = "INSERT INTO tournament_links (id) VALUES (@linkId) ON CONFLICT (id) DO NOTHING;";
                        using (var insertCmd = new NpgsqlCommand(insertCmdText, conn))
                        {
                            insertCmd.Parameters.AddWithValue("@linkId", linkId);
                            await insertCmd.ExecuteNonQueryAsync();
                        }
                    }
                }
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.StackTrace}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error While Processing: {ex.Message} - {ex.StackTrace}");
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
                throw new ApplicationException($"Database error occurred: {ex.StackTrace}", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        private NpgsqlParameter CreateDBIntArrayType(string parameterName, int[] array)
        {
            var newParameters = new NpgsqlParameter();
            newParameters.ParameterName = parameterName;
            newParameters.Value = array;
            newParameters.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer;

            return newParameters;
        }
        private NpgsqlParameter CreateDBTextArrayType(string parameterName, string[] array)
        {
            var newParameters = new NpgsqlParameter();
            newParameters.ParameterName = parameterName;
            newParameters.Value = array;
            newParameters.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text;

            return newParameters;
        }
        private async Task<EventGraphQLResult> QueryStartggEventByTournamentId(int tournamentId)
        {
            var tempQuery = @"query TournamentQuery($tournamentId: ID) {
                              tournaments(query: {
                                filter: {
                                  id:$tournamentId
                                }
                              }) {
                                nodes {id, name, addrState, lat, lng, venueAddress, startAt, endAt, slug, events {
                                    videogame {
                                      id
                                    }}}}}";
            var request = new GraphQLHttpRequest
            {
                Query = tempQuery,
                Variables = new
                {
                    tournamentId = tournamentId
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
        private async Task<EventGraphQLResult> QueryStartggEventsByGameId(IntakeEventsByGameIdCommand intakeCommand)
        {
            var tempQuery = @"query TournamentQuery($perPage: Int, $videogameIds: [ID], $state: String!, $yearStart: Timestamp, $yearEnd: Timestamp) {
                                  tournaments(query: {
                                    perPage: $perPage
                                    filter: {
                                      videogameIds: $videogameIds, addrState: $state, afterDate: $yearStart, beforeDate: $yearEnd
                                    }}) {
                                    nodes {
                                      id name slug startAt endAt events {
                                        videogame {
                                          id
                                        }}}}}";

            var request = new GraphQLHttpRequest
            {
                Query = tempQuery,
                Variables = new
                {
                    perPage = intakeCommand.Page,
                    state = intakeCommand.StateCode,
                    yearStart = intakeCommand.StartDate,
                    yearEnd = intakeCommand.EndDate,
                    videogameIds = intakeCommand.GameIDs
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
        private async Task<EventGraphQLResult> QueryStartggTournamentsByState(IntakeEventsByLocationCommand command)
        {
            var tempQuery = @"query TournamentQuery($perPage: Int, $pagenum:Int, $state: String!, $yearStart: Timestamp, $yearEnd: Timestamp) 
                {tournaments(query: {
                    perPage: $perPage, page: $pagenum
                    filter: {
                        addrState: $state,afterDate: $yearStart,beforeDate: $yearEnd
                            }}) {
                            nodes {
                                id,name,addrState,lat,lng,registrationClosesAt,isRegistrationOpen,venueAddress,startAt,endAt}}}";


            var request = new GraphQLHttpRequest
            {
                Query = tempQuery,
                Variables = new
                {
                    perPage = command.PerPage,
                    page = command.PageNum,
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
    }
}
