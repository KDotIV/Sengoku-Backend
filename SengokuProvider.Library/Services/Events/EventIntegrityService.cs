using Dapper;
using Npgsql;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Regions;

namespace SengokuProvider.Library.Services.Events
{
    public class EventIntegrityService : IEventIntegrityService
    {
        private readonly IEventQueryService _queryService;
        private readonly IEventIntakeService _intakeService;
        private readonly string _connectionString;
        public EventIntegrityService(IEventQueryService eventQueryService, IEventIntakeService eventIntakeService, string connectionString)
        {
            _queryService = eventQueryService;
            _intakeService = eventIntakeService;
            _connectionString = connectionString;
        }

        public async Task<List<int>> BeginEventIntegrity()
        {
            return await GetEventsToUpdate();
        }
        private async Task<List<int>> GetEventsToUpdate()
        {
            var eventsToUpdate = new List<int>();
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    var newQuery = @"SELECT id FROM events
                            WHERE closing_registration_date IS NULL 
                            OR registration_open IS NULL 
                            OR region = 1;";
                    using (var cmd = new NpgsqlCommand(newQuery, conn))
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            eventsToUpdate.Add(reader.GetInt32(0));
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

            return eventsToUpdate;
        }
        public async Task<UpdateEventCommand?> CreateUpdateCommand(int currentEvent)
        {
            try
            {
                var eventToUpdate = new EventData();
                using (var connection = new NpgsqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var query = @"SELECT id, event_name, region, address_id, closing_registration_date, registration_open, link_id
                          FROM events 
                          WHERE id = @Input;";
                    using (var cmd = new NpgsqlCommand(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@Input", currentEvent);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                eventToUpdate.Id = reader.GetInt32(reader.GetOrdinal("id"));
                                eventToUpdate.EventName = reader.GetString(reader.GetOrdinal("event_name"));
                                eventToUpdate.Region = reader.GetInt32(reader.GetOrdinal("region"));
                                eventToUpdate.AddressID = reader.GetInt32(reader.GetOrdinal("address_id"));
                                eventToUpdate.ClosingRegistration = reader.IsDBNull(reader.GetOrdinal("closing_registration_date"))
                                                                    ? null
                                                                    : reader.GetDateTime(reader.GetOrdinal("closing_registration_date"));
                                eventToUpdate.IsRegistrationOpen = reader.IsDBNull(reader.GetOrdinal("registration_open"))
                                                                   ? null
                                                                   : reader.GetBoolean(reader.GetOrdinal("registration_open"));
                                eventToUpdate.LinkID = reader.GetInt32(reader.GetOrdinal("link_id"));
                            }
                        }
                    }
                }
                return await VerifyMissingData(eventToUpdate);
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.StackTrace}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error While Processing: {ex.Message} - {ex.StackTrace}");
            }
            return null;
        }
        private async Task<UpdateEventCommand?> VerifyMissingData(EventData eventToUpdate)
        {
            var newCommand = new UpdateEventCommand { EventId = eventToUpdate.Id };
            var result = await _queryService.QueryStartggEventByEventId(eventToUpdate.LinkID);

            if (result == null) return null;

            var firstNode = result.Tournaments.Nodes.FirstOrDefault();
            if (firstNode == null) { Console.WriteLine("No Nodes were Retrieved. Check EventId"); return null; }
            var tempDateTime = DateTimeOffset.FromUnixTimeSeconds(firstNode.RegistrationClosesAt).DateTime;

            if (!eventToUpdate.ClosingRegistration.HasValue || eventToUpdate.ClosingRegistration != tempDateTime)
            {
                newCommand.UpdateParameters.Add(new Tuple<string, string>("closing_registration_date", tempDateTime.ToString("yyyy-MM-dd")));
            }
            if (!eventToUpdate.IsRegistrationOpen.HasValue || eventToUpdate.IsRegistrationOpen != firstNode.IsRegistrationOpen)
            {
                newCommand.UpdateParameters.Add(new Tuple<string, string>("registration_open", firstNode.IsRegistrationOpen.ToString()));
            }
            if (eventToUpdate.Region == 1)
            {
                var addressData = await _queryService.GetAddressById(eventToUpdate.AddressID);
                if (addressData == null)
                {
                    throw new ApplicationException($"Unable to find Address by given ID: {eventToUpdate.AddressID}");
                }

                var regionData = await VerifyRegionAddressLink(addressData);
                if (regionData == null)
                {
                    Console.WriteLine("Creating New Region");
                    var regionResult = await _intakeService.IntakeNewRegion(addressData);
                    if (regionResult <= 0) { throw new ApplicationException($"Unable to Insert New Region by Address: {eventToUpdate.AddressID}"); }

                    newCommand.UpdateParameters.Add(new Tuple<string, string>("region", regionResult.ToString()));
                }
                else { newCommand.UpdateParameters.Add(new Tuple<string, string>("region", regionData.Id.ToString())); }
            }
            return newCommand;
        }
        private async Task<RegionData?> VerifyRegionAddressLink(AddressData addressData)
        {
            if (string.IsNullOrEmpty(addressData.Address)) return null;

            var addressSplit = addressData.Address.Split(",");
            if (addressSplit.Length < 3) return null;

            var tempCity = addressSplit[1].Trim();
            string tempZipCode = "";
            if (addressSplit.Length > 2) { tempZipCode = addressSplit[2].Trim().Split(" ")[1]; }

            var cityQuery = new GetRegionCommand { QueryParameter = new Tuple<string, string>("name", tempCity) };
            var cityResult = await _queryService.QueryRegion(cityQuery);
            if (cityResult == null && !string.IsNullOrEmpty(tempZipCode))
            {
                var zipCodeQuery = new GetRegionCommand { QueryParameter = new Tuple<string, string>("id", tempZipCode) };
                var zipCodeResult = await _queryService.QueryRegion(zipCodeQuery);
                return zipCodeResult;
            }
            return cityResult;
        }
        public async Task<List<int>> BeginIntegrityTournamentLinks()
        {
            return await GetTournamentLinksToProcess();
        }
        public async Task<bool> VerifyTournamentLinkChange(int linkId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var newQuery = @"Select url_slug, games_ids from tournament_links
                                   WHERE id = @Input";
                    var link = await conn.QueryFirstOrDefaultAsync<TournamentData>(newQuery, new { Input = linkId });
                    if (link == null || link.Games == null ||
                        link.Games.Length == 0 || string.IsNullOrEmpty(link.UrlSlug))
                    {
                        return false;
                    }
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
        private async Task<List<int>> GetTournamentLinksToProcess()
        {
            var linksToProcess = new List<int>();
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var newQuery = @"Select id from tournament_links
                                   WHERE url_slug IS NULL and games_ids IS NULL;";
                    using (var reader = await conn.ExecuteReaderAsync(newQuery))
                    {
                        while (await reader.ReadAsync())
                        {
                            linksToProcess.Add(reader.GetInt32(0));
                        }
                    }
                }
                return linksToProcess;
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.StackTrace}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error While Processing: {ex.Message} - {ex.StackTrace}");
            }
            return linksToProcess;
        }
        public Task<bool> VerifyEventUpdate()
        {
            throw new NotImplementedException();
        }
    }
}
