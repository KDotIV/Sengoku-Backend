using Dapper;
using Npgsql;
using SengokuProvider.API.Models.Events;
using SengokuProvider.API.Models.Regions;
using SengokuProvider.API.Services.Common;

namespace SengokuProvider.API.Services.Events
{
    internal class EventQueryService : IEventQueryService
    {
        private readonly string _connectionString;
        private readonly IntakeValidator _validator;
        internal EventQueryService(string connectionString, IntakeValidator validator)
        {
            _connectionString = connectionString;
            _validator = validator;
        }

        public async Task<List<AddressEventResult>> QueryEventsByLocation(GetTournamentsByLocationCommand command, int pageNumber)
        {
            if (command == null || command.RegionId == 0) throw new ArgumentNullException(nameof(command));
            try
            {
                var sortedAddresses = new List<AddressEventResult>();

                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var newQuery = @"SELECT name, latitude, longitude, province FROM regions WHERE id = @Input";
                    var regionResult = await conn.QueryFirstOrDefaultAsync<RegionData>(newQuery, new { Input = command.RegionId });

                    using (var cmd = new NpgsqlCommand())
                    {
                        cmd.Connection = conn;
                        cmd.CommandText = @"
                            SELECT 
                                a.address, a.latitude, a.longitude, 
                                e.event_name, e.event_description, e.region, e.start_time, e.end_time, e.link_id,
                                SQRT(
                                    POW(a.longitude - @startLongitude, 2) + POW(a.latitude - @startLatitude, 2)
                                ) as distance
                            FROM 
                                addresses a
                            JOIN 
                                events e ON a.id = e.address_id
                            WHERE
                                e.start_time >= CURRENT_DATE
                            ORDER BY
                                e.start_time ASC,
                                distance ASC
                            LIMIT @perPage;";
                        cmd.Parameters.AddWithValue("@startLatitude", regionResult.Latitude);
                        cmd.Parameters.AddWithValue("@startLongitude", regionResult.Longitude);
                        cmd.Parameters.AddWithValue("@perPage", command.PerPage);

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
                                    LinkId = reader.GetInt32(reader.GetOrdinal("link_id"))
                                });
                            }
                        }
                    }
                }

                return sortedAddresses;
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
