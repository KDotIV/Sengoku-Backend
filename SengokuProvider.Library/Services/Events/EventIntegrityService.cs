using Dapper;
using Npgsql;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Regions;
using System.Text.RegularExpressions;

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
                        if (!reader.HasRows) return eventsToUpdate;
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
                var eventToUpdate = new EventData { LastUpdate = DateTime.UtcNow };
                using (var connection = new NpgsqlConnection(_connectionString))
                {
                    await connection.OpenAsync();

                    var query = @"SELECT id, event_name, region, address_id, closing_registration_date, registration_open, link_id, online_tournament, url_slug
                          FROM events 
                          WHERE id = @Input;";
                    using (var cmd = new NpgsqlCommand(query, connection))
                    {
                        cmd.Parameters.AddWithValue("@Input", currentEvent);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows)
                            {
                                Console.WriteLine("No standings found for the provided player ID.");
                                return new UpdateEventCommand { Topic = CommandRegistry.UpdateEvent, UpdateParameters = new List<Tuple<string, string>>() };
                            }
                            while (await reader.ReadAsync())
                            {
                                eventToUpdate.Id = reader.GetInt32(reader.GetOrdinal("id"));
                                eventToUpdate.EventName = reader.GetString(reader.GetOrdinal("event_name"));
                                eventToUpdate.Region = reader.GetString(reader.GetOrdinal("region"));
                                eventToUpdate.AddressID = reader.GetInt32(reader.GetOrdinal("address_id"));
                                eventToUpdate.ClosingRegistration = reader.IsDBNull(reader.GetOrdinal("closing_registration_date"))
                                                                    ? null
                                                                    : reader.GetDateTime(reader.GetOrdinal("closing_registration_date"));
                                eventToUpdate.IsRegistrationOpen = reader.IsDBNull(reader.GetOrdinal("registration_open")) ? null : reader.GetBoolean(reader.GetOrdinal("registration_open"));
                                eventToUpdate.LinkID = reader.GetInt32(reader.GetOrdinal("link_id"));
                                eventToUpdate.IsOnline = reader.IsDBNull(reader.GetOrdinal("online_tournament")) ? null : reader.GetBoolean(reader.GetOrdinal("online_tournament"));
                                eventToUpdate.UrlSlug = reader.IsDBNull(reader.GetOrdinal("url_slug")) ? null : reader.GetString(reader.GetOrdinal("url_slug"));
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
            var newCommand = new UpdateEventCommand { EventId = eventToUpdate.Id, Topic = CommandRegistry.UpdateEvent, UpdateParameters = new List<Tuple<string, string>>() };
            var result = await _queryService.QueryStartggEventByEventId(eventToUpdate.LinkID);

            if (result == null) return null;

            var firstNode = result.Events.Nodes.FirstOrDefault();
            if (firstNode == null) { Console.WriteLine("No Nodes were Retrieved. Check EventId"); return null; }
            var tempDateTime = DateTimeOffset.FromUnixTimeSeconds(firstNode.RegistrationClosesAt).DateTime;

            newCommand = await BuildUpdateCommand(eventToUpdate, newCommand, firstNode, tempDateTime);
            return newCommand;
        }
        private async Task<UpdateEventCommand> BuildUpdateCommand(EventData eventToUpdate, UpdateEventCommand newCommand, EventNode? firstNode, DateTime tempDateTime)
        {
            if (!eventToUpdate.ClosingRegistration.HasValue || eventToUpdate.ClosingRegistration != tempDateTime)
            {
                newCommand.UpdateParameters.Add(new Tuple<string, string>("closing_registration_date", tempDateTime.ToString("yyyy-MM-dd")));
            }
            if (!eventToUpdate.IsRegistrationOpen.HasValue || eventToUpdate.IsRegistrationOpen != firstNode.IsRegistrationOpen)
            {
                newCommand.UpdateParameters.Add(new Tuple<string, string>("registration_open", firstNode.IsRegistrationOpen.ToString()));
            }
            if (!eventToUpdate.IsOnline.HasValue || eventToUpdate.IsOnline != firstNode.IsOnline)
            {
                newCommand.UpdateParameters.Add(new Tuple<string, string>("online_tournament", firstNode.IsOnline.ToString()));
            }
            if (string.IsNullOrEmpty(eventToUpdate.UrlSlug) || eventToUpdate.UrlSlug != firstNode.Slug)
            {
                newCommand.UpdateParameters.Add(new Tuple<string, string>("url_slug", firstNode.Slug));
            }
            if (eventToUpdate.Region is not null || eventToUpdate.Region != "00000")
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
                    if (regionResult == "00000") { throw new ApplicationException($"Unable to Insert New Region by Address: {eventToUpdate.AddressID}"); }

                    newCommand.UpdateParameters.Add(new Tuple<string, string>("region", regionResult.ToString()));
                }
                else { newCommand.UpdateParameters.Add(new Tuple<string, string>("region", regionData.Id.ToString())); }
            }
            return newCommand;
        }
        private async Task<RegionData?> VerifyRegionAddressLink(AddressData addressData)
        {
            if (string.IsNullOrEmpty(addressData.Address)) return null;

            // Regex to extract postal code (assumes postal code is always numbers and possibly a space within, adjust as necessary)
            var postalCodeRegex = new Regex(@"\b\d{3,6}\b");
            var match = postalCodeRegex.Match(addressData.Address);
            string postalCode = match.Success ? match.Value.Trim() : "";

            // Split address and attempt to identify city by removing known non-city components
            var addressParts = addressData.Address.Split(new[] { ',', '-' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(part => part.Trim()).ToList();

            // Remove postal code and country if identified
            if (!string.IsNullOrEmpty(postalCode))
            {
                addressParts.Remove(postalCode);
            }
            addressParts.RemoveAll(part => part.Length == 2 || part.All(char.IsDigit)); // Assuming 2-letter parts are states/countries

            // The last remaining element could be the city, typically after removing postal code and country
            string city = addressParts.FirstOrDefault();

            if (!string.IsNullOrEmpty(city))
            {
                var cityResult = await _queryService.QueryRegion(new GetRegionCommand { QueryParameter = new Tuple<string, string>("name", city) });
                if (cityResult != null) return cityResult;
            }

            if (!string.IsNullOrEmpty(postalCode))
            {
                var zipCodeResult = await _queryService.QueryRegion(new GetRegionCommand { QueryParameter = new Tuple<string, string>("id", postalCode) });
                return zipCodeResult;
            }

            return null;
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

                    var newQuery = @"Select url_slug, game_id from tournament_links
                                   WHERE id = @Input";
                    var link = await conn.QueryFirstOrDefaultAsync<TournamentData>(newQuery, new { Input = linkId });
                    if (link == null || string.IsNullOrEmpty(link.UrlSlug))
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

                    var linkQuery = @"Select id FROM tournament_links WHERE url_slug IS NULL OR last_updated IS NULL OR event_id IS NULL OR last_updated >= NOW() - INTERVAL '1 HOURS'";
                    using (var reader = await conn.ExecuteReaderAsync(linkQuery))
                    {
                        if (!reader.HasRows) return linksToProcess;
                        while (await reader.ReadAsync())
                        {
                            linksToProcess.Add(reader.GetInt32(reader.GetOrdinal("id")));
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
