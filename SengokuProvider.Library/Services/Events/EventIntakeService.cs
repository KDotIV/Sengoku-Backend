using Dapper;
using GraphQL.Client.Http;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Geometries;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Npgsql;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Models.Regions;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Worker.Handlers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace SengokuProvider.Library.Services.Events
{
    public class EventIntakeService : IEventIntakeService
    {
        private readonly IntakeValidator _validator;
        private readonly GraphQLHttpClient _client;
        private readonly IEventQueryService _queryService;
        private readonly RequestThrottler _requestThrottler;
        private readonly IConfiguration _config;
        private readonly IAzureBusApiService _azureBusApiService;

        private readonly string _connectionString;
        private ConcurrentDictionary<string, int> _addressCache;
        public EventIntakeService(string connectionString, IConfiguration configuration, GraphQLHttpClient client, IEventQueryService eventQueryService,
            IAzureBusApiService busApiService, IntakeValidator validator, RequestThrottler throttler)
        {
            _config = configuration;
            _connectionString = connectionString;
            _validator = validator;
            _client = client;
            _queryService = eventQueryService;
            _requestThrottler = throttler;
            _azureBusApiService = busApiService;
            _addressCache = new ConcurrentDictionary<string, int>();

            _client.HttpClient.DefaultRequestHeaders.Clear();
            _client.HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config["GraphQLSettings:EventBearer"]);
        }
        #region Create Tournament Data
        public async Task<List<int>> IntakeTournamentData(IntakeEventsByLocationCommand intakeCommand)
        {
            try
            {
                EventGraphQLResult? newEventData = await QueryStartggTournamentsByState(intakeCommand);
                if (newEventData == null) { return new List<int> { 0, 0, 0 }; }

                var intakeResult = new List<int>();
                var eventsResult = await ProcessEventData(newEventData);
                intakeResult.Add(eventsResult.Item1);
                intakeResult.Add(eventsResult.Item2);

                Console.WriteLine($"Addresses Inserted: {eventsResult.Item1} - Events Inserted: {eventsResult.Item2}");

                intakeResult.Add(await ProcessTournamentData(newEventData));

                return intakeResult;
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        public async Task<int> IntakeTournamentIdData(LinkTournamentByEventIdCommand command)
        {
            EventGraphQLResult? newEventData = await QueryStartggTournamentDataByEventLink(command.EventLinkId);
            if (newEventData == null) { return 0; }

            _ = await ProcessEventData(newEventData);
            return await ProcessTournamentData(newEventData);
        }
        public async Task<int> IntakeEventsByGameId(IntakeEventsByGameIdCommand intakeCommand)
        {
            try
            {
                EventGraphQLResult? newEventData = await QueryStartggEventsByGameId(intakeCommand);
                if (newEventData == null) { return 0; }

                return await ProcessTournamentData(newEventData);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        public async Task<bool> SendTournamentLinkEventMessage(int eventLinkId)
        {
            if (string.IsNullOrEmpty(_config["ServiceBusSettings:eventreceivedqueue"]) || _config == null)
            {
                Console.WriteLine("Service Bus Settings Cannot be empty or null");
                return false;
            }
            try
            {
                var newCommand = new EventReceivedData
                {
                    Command = new LinkTournamentByEventIdCommand
                    {
                        EventLinkId = eventLinkId,
                        Topic = CommandRegistry.LinkTournamentByEvent,
                    },
                    MessagePriority = MessagePriority.SystemIntake
                };
                var messageJson = JsonConvert.SerializeObject(newCommand, JsonSettings.DefaultSettings);
                var result = await _azureBusApiService.SendBatchAsync(_config["ServiceBusSettings:eventreceivedqueue"], messageJson);

                if (!result)
                {
                    Console.WriteLine("Failed to Send Service Bus Message to Event Received Queue. Check Data");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        public async Task<bool> SendEventIntakeLocationMessage(IntakeEventsByLocationCommand command)
        {
            if (string.IsNullOrEmpty(_config["ServiceBusSettings:eventreceivedqueue"]) || _config == null)
            {
                Console.WriteLine("Service Bus Settings Cannot be empty or null");
                return false;
            }
            try
            {
                var newCommand = new EventReceivedData
                {
                    Command = new IntakeEventsByLocationCommand
                    {
                        PerPage = command.PerPage,
                        StateCode = command.StateCode,
                        StartDate = command.StartDate,
                        EndDate = command.EndDate,
                        Topic = CommandRegistry.IntakeEventsByLocation
                    },
                    MessagePriority = MessagePriority.SystemIntake
                };
                var messageJson = JsonConvert.SerializeObject(newCommand, JsonSettings.DefaultSettings);
                var result = await _azureBusApiService.SendBatchAsync(_config["ServiceBusSettings:eventreceivedqueue"], messageJson);

                if (!result)
                {
                    Console.WriteLine("Failed to Send Service Bus Message to Event Received Queue. Check Data");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        private async Task<bool> SendTournamentPlayerIntakeMessage(int tournamentLink)
        {
            if (string.IsNullOrEmpty(_config["ServiceBusSettings:PlayerReceivedQueue"]) || _config == null)
            {
                Console.WriteLine("Service Bus Settings Cannot be empty or null");
                return false;
            }
            try
            {
                var newCommand = new PlayerReceivedData
                {
                    Command = new IntakePlayersByTournamentCommand
                    {
                        Topic = CommandRegistry.IntakePlayersByTournament,
                        TournamentLink = tournamentLink,
                    },
                    MessagePriority = MessagePriority.SystemIntake
                };
                var messageJson = JsonConvert.SerializeObject(newCommand, JsonSettings.DefaultSettings);
                var result = await _azureBusApiService.SendBatchAsync(_config["ServiceBusSettings:PlayerReceivedQueue"], messageJson);

                if (!result)
                {
                    Console.WriteLine("Failed to Send Service Bus Message to Event Received Queue. Check Data");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        private async Task<int> ProcessTournamentData(EventGraphQLResult newEventData)
        {
            var totalSuccess = 0;
            try
            {
                var currentBatch = BuildTournamentData(newEventData);
                if (currentBatch == null || currentBatch.Count == 0) { return 0; }
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
            Console.WriteLine($"Current Tournament Intake Batch:{currentBatch.Count}");
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var transaction = await conn.BeginTransactionAsync())
                {
                    foreach (var tournament in currentBatch)
                    {
                        var createInsertCommand = @"
                            INSERT INTO tournament_links (id, url_slug, game_id, event_id, last_updated)
                            VALUES (@Input, @UrlSlug, @Game, @EventId, @LastUpdated)
                            ON CONFLICT (id) DO UPDATE SET
                                url_slug = EXCLUDED.url_slug,
                                game_id = EXCLUDED.game_id,
                                event_id = EXCLUDED.event_id,
                                last_updated = EXCLUDED.last_updated;";
                        using (var command = new NpgsqlCommand(createInsertCommand, conn))
                        {
                            command.Transaction = transaction;
                            command.Parameters.AddWithValue(@"Input", tournament.Id);
                            command.Parameters.AddWithValue(@"UrlSlug", tournament.UrlSlug);
                            command.Parameters.AddWithValue(@"Game", tournament.GameId);
                            command.Parameters.AddWithValue(@"EventId", tournament.EventId);
                            command.Parameters.AddWithValue(@"LastUpdated", tournament.LastUpdated);

                            var result = await command.ExecuteNonQueryAsync();
                            if (result > 0)
                            {
                                totalSuccess += result;
                                Console.WriteLine($"Current Success: {totalSuccess}");
                                if (await SendTournamentLinkEventMessage(tournament.EventId)) { Console.WriteLine($"Event: {tournament.EventId} - Tournament: {tournament.Id} Intake Message Sent"); }
                            }
                        }
                    }
                    await transaction.CommitAsync();
                }
            }

            return totalSuccess;
        }
        private List<TournamentData>? BuildTournamentData(EventGraphQLResult newEventData)
        {
            if (newEventData == null) { return null; }
            var tournamentBatch = new List<TournamentData>();

            foreach (var currentEvent in newEventData.Events.Nodes)
            {
                if (currentEvent.Tournaments == null) continue;
                if (currentEvent.Tournaments.Count == 0) continue;

                foreach (var tournament in currentEvent.Tournaments)
                {
                    TournamentData newTournamentData = new TournamentData
                    {
                        Id = tournament.Id,
                        UrlSlug = tournament.UrlSlug,
                        EventId = currentEvent.Id,
                        GameId = tournament.Videogame.Id,
                        LastUpdated = DateTime.UtcNow
                    };
                    tournamentBatch.Add(newTournamentData);
                }
            }
            return tournamentBatch;
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
                                    _addressCache.TryAdd(newAddress.Address ?? string.Empty, addressId);
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
                            if (newEvent == null) continue;
                            await EnsureLinkIdExists(newEvent.LinkID, conn);

                            var createNewInsertCommand = @"INSERT INTO events (event_name, event_description, region, address_id, start_time, end_time, link_id, closing_registration_date,registration_open,online_tournament,last_updated) 
                            VALUES (@Event_Name, @Event_Description, @Region, @Address_Id, @Start_Time, @End_Time, @Link_Id, @ClosingRegistration, @IsRegistrationOpen, @IsOnline, @Updated)
                            ON CONFLICT (link_id) DO UPDATE SET
                                event_name = EXCLUDED.event_name,
                                event_description = EXCLUDED.event_description,
                                region = EXCLUDED.region,
                                address_id = EXCLUDED.address_id,
                                start_time = EXCLUDED.start_time,
                                end_time = EXCLUDED.end_time,
                                closing_registration_date = EXCLUDED.closing_registration_date,
                                registration_open = EXCLUDED.registration_open,
                                online_tournament = EXCLUDED.online_tournament;";
                            using (var command = new NpgsqlCommand(createNewInsertCommand, conn))
                            {
                                command.Parameters.AddWithValue("@Event_Name", newEvent.EventName ?? string.Empty);
                                command.Parameters.AddWithValue(@"Event_Description", newEvent.EventDescription ?? string.Empty);
                                command.Parameters.AddWithValue(@"Region", newEvent.Region);
                                command.Parameters.AddWithValue("@Address_Id", newEvent.AddressID);
                                command.Parameters.AddWithValue(@"Start_Time", newEvent.StartTime);
                                command.Parameters.AddWithValue(@"End_Time", newEvent.EndTime);
                                command.Parameters.AddWithValue(@"Link_Id", newEvent.LinkID);
                                command.Parameters.AddWithValue(@"ClosingRegistration", newEvent.ClosingRegistration);
                                command.Parameters.AddWithValue(@"IsRegistrationOpen", newEvent.IsRegistrationOpen);
                                command.Parameters.AddWithValue(@"IsOnline", newEvent.IsOnline);
                                command.Parameters.AddWithValue(@"Updated", newEvent.LastUpdate);
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

            foreach (var node in queryData.Events.Nodes)
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
                _addressCache[address.Address ?? string.Empty] = address.Id;
                addressMap[address.Address ?? string.Empty] = address.Id;
            }

            // Process events after all addresses are handled
            foreach (var node in queryData.Events.Nodes)
            {
                if (!await CheckDuplicateEvents(node.Id))
                {
                    int regionId = await GetRegionId(node.City);
                    var eventData = new EventData
                    {
                        LinkID = node.Id,
                        EventName = node.Name,
                        EventDescription = "Sample description",
                        Region = regionId,
                        AddressID = addressMap[node.VenueAddress],  // Use the confirmed address ID from the map
                        StartTime = DateTimeOffset.FromUnixTimeSeconds(node.StartAt).DateTime,
                        EndTime = DateTimeOffset.FromUnixTimeSeconds(node.EndAt).DateTime,
                        ClosingRegistration = DateTimeOffset.FromUnixTimeSeconds(node.RegistrationClosesAt).DateTime,
                        IsRegistrationOpen = node.IsRegistrationOpen,
                        IsOnline = node.IsOnline,
                        LastUpdate = DateTime.UtcNow
                    };
                    events.Add(eventData);
                }
            }

            // Insert events data
            int eventSuccesses = await InsertNewEventsData(events);

            return Tuple.Create(addressSuccesses, eventSuccesses);
        }
        private async Task<int> GetRegionId(string? city)
        {
            var queryResult = await _queryService.QueryRegion(new GetRegionCommand { QueryParameter = new Tuple<string, string>("name", city ?? string.Empty) });
            return queryResult?.Id ?? 1;
        }
        public async Task<int> IntakeNewRegion(AddressData addressData)
        {
            if (string.IsNullOrEmpty(addressData.Address)) return 0;

            var addressSplit = addressData.Address.Split(",");
            if (addressSplit.Length < 3)
            {
                Console.WriteLine("Address data is insufficient for processing.");
                return 0;
            }

            var tempCity = addressSplit[1].Trim();
            int tempZipCode = 0;

            // Ensure the split operation has the expected length to avoid out-of-bounds errors
            if (addressSplit.Length > 2)
            {
                var zipCodePart = addressSplit[2].Trim().Split(" ");
                if (zipCodePart.Length > 1 && !int.TryParse(zipCodePart[1], out tempZipCode))
                {
                    Console.WriteLine("Failed to parse ZIP code from address.");
                    return 0;
                }
            }

            var cityQuery = new GetRegionCommand { QueryParameter = new Tuple<string, string>("name", tempCity) };
            var result = await VerifyRegion(cityQuery);

            if (result == null)
            {
                Console.WriteLine($"Determining Province for City: {tempCity}");
                var regionResult = DetermineProvince(addressData);
                try
                {
                    var insertResult = await InsertNewRegionData(new RegionData
                    {
                        Id = tempZipCode,
                        Name = tempCity,
                        Latitude = addressData.Latitude ?? 0.0,
                        Longitude = addressData.Longitude ?? 0.0,
                        Province = regionResult
                    });

                    if (insertResult > 0)
                    {
                        return tempZipCode;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error While Inserting Region Data: {ex.Message} - {ex.StackTrace}");
                }
            }

            return 0;
        }
        private string DetermineProvince(AddressData addressData)
        {
            if (!addressData.Latitude.HasValue || !addressData.Longitude.HasValue)
                return "DF";

            var point = new Point(addressData.Longitude.Value, addressData.Latitude.Value) { SRID = 4326 };

            foreach (var region in GeoConstants.Regions)
            {
                if (region.Value.Contains(point))
                {
                    return region.Key;
                }
            }
            return "DF";
        }
        private async Task<int> InsertNewRegionData(RegionData newData)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"INSERT INTO regions (id, name, latitude, longitude, Province)
                                                    VALUES (@ZipCode, @NameInput, @LatInput, @LongInput, @ProvInput)
                                                    ON CONFLICT (id) DO NOTHING", conn))
                    {
                        cmd.Parameters.AddWithValue("@ZipCode", newData.Id);
                        cmd.Parameters.AddWithValue("@NameInput", newData.Name);
                        cmd.Parameters.AddWithValue("@LatInput", newData.Latitude);
                        cmd.Parameters.AddWithValue("@LongInput", newData.Longitude);
                        cmd.Parameters.AddWithValue("@ProvInput", newData.Province);

                        var result = await cmd.ExecuteNonQueryAsync();
                        return result;
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
            return 0;
        }
        private NpgsqlParameter CreateDBIntArrayType(string parameterName, int[] array)
        {
            var newParameters = new NpgsqlParameter();
            newParameters.ParameterName = parameterName;
            newParameters.Value = array ?? Array.Empty<int>();
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
        #endregion

        #region Query Commands
        private async Task<EventGraphQLResult?> QueryStartggTournamentDataByEventLink(int eventLinkId)
        {
            var tempQuery = @"query TournamentQuery($tournamentId: ID) {
                      tournaments(query: {
                        filter: {
                          id: $tournamentId
                        }
                      }) {
                        nodes {id, name, addrState, lat, lng, venueAddress, startAt, endAt, slug, events {
                            id,
                            slug,
                            videogame { id }}}}}";

            var request = new GraphQLHttpRequest
            {
                Query = tempQuery,
                Variables = new
                {
                    eventLinkId
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
                    if (response.Data == null) throw new Exception($"Failed to retrieve tournament data.");

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
        private async Task<EventGraphQLResult?> QueryStartggEventsByGameId(IntakeEventsByGameIdCommand intakeCommand)
        {
            var tempQuery = @"query TournamentQuery($perPage: Int, $videogameIds: [ID], $state: String!, $yearStart: Timestamp, $yearEnd: Timestamp) {
                          tournaments(query: {
                            perPage: $perPage
                            filter: {
                              videogameIds: $videogameIds, addrState: $state, afterDate: $yearStart, beforeDate: $yearEnd
                            }}) {
                            nodes { id name slug startAt endAt events { id, slug, videogame { id }}}}}";

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
                    if (response.Data == null) throw new Exception($"Failed to retrieve tournament data.");

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
        private async Task<EventGraphQLResult?> QueryStartggTournamentsByState(IntakeEventsByLocationCommand command)
        {
            var tempQuery = @"query TournamentQuery($perPage: Int, $pagenum:Int, $state: String!, $yearStart: Timestamp, $yearEnd: Timestamp) 
        { tournaments(query: {
            perPage: $perPage, page: $pagenum
            filter: {
                addrState: $state,afterDate: $yearStart,beforeDate: $yearEnd
                    }}) {
                    nodes { id,name,addrState,lat,lng,registrationClosesAt,isRegistrationOpen,city,isOnline,venueAddress,startAt,endAt, events { id, slug, videogame { id }}}
                    pageInfo { total totalPages page perPage sortBy filter }}}";

            var allNodes = new List<EventNode>();
            int currentPage = 1;
            bool hasNextPage = true;

            while (hasNextPage)
            {
                var request = new GraphQLHttpRequest
                {
                    Query = tempQuery,
                    Variables = new
                    {
                        perPage = command.PerPage,
                        pagenum = currentPage,
                        state = command.StateCode,
                        yearStart = command.StartDate,
                        yearEnd = command.EndDate,
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
                        if (response.Data == null) throw new Exception($"Failed to retrieve tournament data. {response.Errors.ToString()}");

                        var tempJson = JsonConvert.SerializeObject(response.Data, Formatting.Indented);
                        var eventData = JsonConvert.DeserializeObject<EventGraphQLResult>(tempJson);

                        if (eventData?.Events?.Nodes != null)
                        {
                            allNodes.AddRange(eventData.Events.Nodes);
                        }

                        var pageInfo = eventData.Events.PageInfo;
                        int totalPages = pageInfo.TotalPages;
                        currentPage = pageInfo.Page + 1;
                        Console.WriteLine($"On Location Events Page: {currentPage}");
                        hasNextPage = currentPage < totalPages;
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
            var eventResult = new EventGraphQLResult
            {
                Events = new EventResult { Nodes = allNodes }
            };
            return eventResult;
        }
        #endregion

        #region Update Commands
        public async Task<bool> UpdateEventData(UpdateEventCommand command)
        {
            if (!command.Validate())
            {
                throw new ArgumentException("Invalid command parameters");
            }

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var updateCommand = new StringBuilder("UPDATE public.events SET ");
                    var parameters = new List<NpgsqlParameter>();

                    for (int i = 0; i < command.UpdateParameters.Count; i++)
                    {
                        var param = command.UpdateParameters[i];
                        var paramValue = $"@ParamValue{i}";

                        // Add the column assignment to the SQL command
                        updateCommand.Append($"{param.Item1} = {paramValue}");
                        if (i < command.UpdateParameters.Count - 1)
                        {
                            updateCommand.Append(", ");
                        }

                        // Determine the parameter type and add the parameter to the list
                        var parameter = new NpgsqlParameter(paramValue, param.Item2);

                        if (int.TryParse(param.Item2, out int intValue))
                        {
                            parameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Integer;
                            parameter.Value = intValue;
                        }
                        else if (DateTime.TryParse(param.Item2, out DateTime dateValue))
                        {
                            parameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Timestamp;
                            parameter.Value = dateValue;
                        }
                        else if (bool.TryParse(param.Item2, out bool boolValue))
                        {
                            parameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Boolean;
                            parameter.Value = boolValue;
                        }
                        else
                        {
                            parameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text;
                        }

                        parameters.Add(parameter);
                    }

                    // Complete the SQL command with the WHERE clause
                    updateCommand.Append(" WHERE id = @EventId");
                    parameters.Add(new NpgsqlParameter("@EventId", NpgsqlTypes.NpgsqlDbType.Integer) { Value = command.EventId });

                    // Execute the dynamically built SQL command
                    using (var cmd = new NpgsqlCommand(updateCommand.ToString(), conn))
                    {
                        cmd.Parameters.AddRange(parameters.ToArray());
                        var result = await cmd.ExecuteNonQueryAsync();
                        return result > 0;
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

            return false;
        }
        #endregion

        #region Verify Data
        private async Task<RegionData?> VerifyRegion(GetRegionCommand regionQuery)
        {
            return await _queryService.QueryRegion(regionQuery);
        }
        private async Task<bool> VerifyTournamentLink(int tournamentLinkId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var newQuery = @"SELECT link_id FROM events WHERE link_id = @Input;";
                    var result = await conn.QueryFirstOrDefaultAsync<int>(newQuery, new { Input = tournamentLinkId });

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
        private async Task<int> CheckDuplicateAddress(string address)
        {
            try
            {
                if (_addressCache.TryGetValue(address, out int addressId) && addressId != 0) return addressId;
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

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
        #endregion
    }
}
