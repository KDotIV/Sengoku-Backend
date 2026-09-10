using Dapper;
using Npgsql;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Regions;
using System.Text;
using System.Collections.Concurrent;

namespace SengokuProvider.Library.Services.Events;

public sealed class EventIntakeService : IEventIntakeService
{
    private readonly string _connectionString;
    private readonly ConcurrentDictionary<string, int> _addressCache = new();
    public EventIntakeService(string connectionString) => _connectionString = connectionString;
    public async Task<int> InsertNewTournamentData(int totalSuccess, List<TournamentData> currentBatch)
        {
            Console.WriteLine($"Current Tournament Intake Batch:{currentBatch.Count}");
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var transaction = await conn.BeginTransactionAsync())
                    {
                        var insertQuery = new StringBuilder(@"INSERT INTO tournament_links (id, url_slug, game_id, event_link, entrants_num, last_updated) VALUES ");
                        var queryParams = new List<NpgsqlParameter>();
                        var valueCount = 0;
                        foreach (var tournament in currentBatch)
                        {
                            if (valueCount > 0)
                            {
                                insertQuery.Append(", ");
                            }

                            insertQuery.Append($"(@Input{valueCount}, @UrlSlug{valueCount}, @Game{valueCount}, @EventId{valueCount}, @EntrantsNum{valueCount}, @LastUpdated{valueCount})");

                            queryParams.Add(new NpgsqlParameter($"@Input{valueCount}", tournament.Id));
                            queryParams.Add(new NpgsqlParameter($"@UrlSlug{valueCount}", tournament.UrlSlug));
                            queryParams.Add(new NpgsqlParameter($"@Game{valueCount}", tournament.GameId));
                            queryParams.Add(new NpgsqlParameter($"@EventId{valueCount}", tournament.EventId));
                            queryParams.Add(new NpgsqlParameter($"@EntrantsNum{valueCount}", tournament.EntrantsNum));
                            queryParams.Add(new NpgsqlParameter($"@LastUpdated{valueCount}", tournament.LastUpdated));

                            Console.WriteLine($"Event: {tournament.EventId} - Tournament: {tournament.Id} Added");

                            valueCount++;
                        }
                        insertQuery.Append(" ON CONFLICT (id) DO UPDATE SET url_slug = EXCLUDED.url_slug,game_id = EXCLUDED.game_id,event_link = EXCLUDED.event_link,last_updated = EXCLUDED.last_updated,entrants_num = EXCLUDED.entrants_num;");

                        using (var cmd = new NpgsqlCommand(insertQuery.ToString(), conn))
                        {
                            cmd.Parameters.AddRange(queryParams.ToArray());
                            var result = await cmd.ExecuteNonQueryAsync();
                            if (result > 0)
                            {
                                totalSuccess = result;
                                Console.WriteLine($"Current Success: {result}");
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
    public async Task<int> InsertNewAddressData(List<AddressData> data)
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
    public async Task<int> InsertNewEventsData(List<EventData> data)
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

                            var createNewInsertCommand = @"INSERT INTO events (event_name, event_description, region, address_id, start_time, end_time, link_id, closing_registration_date,registration_open,online_tournament, url_slug, last_updated) 
                            VALUES (@Event_Name, @Event_Description, @Region, @Address_Id, @Start_Time, @End_Time, @Link_Id, @ClosingRegistration, @IsRegistrationOpen, @IsOnline, @Slug, @Updated)
                            ON CONFLICT (link_id) DO UPDATE SET
                                event_name = EXCLUDED.event_name,
                                event_description = EXCLUDED.event_description,
                                start_time = EXCLUDED.start_time,
                                end_time = EXCLUDED.end_time,
                                closing_registration_date = EXCLUDED.closing_registration_date,
                                registration_open = EXCLUDED.registration_open,
                                online_tournament = EXCLUDED.online_tournament,
                                url_slug = EXCLUDED.url_slug;";
                            using (var command = new NpgsqlCommand(createNewInsertCommand, conn))
                            {
                                command.Parameters.AddWithValue("@Event_Name", newEvent.EventName ?? string.Empty);
                                command.Parameters.AddWithValue(@"Event_Description", newEvent.EventDescription ?? string.Empty);
                                command.Parameters.AddWithValue(@"Region", newEvent.Region ?? string.Empty);
                                command.Parameters.AddWithValue("@Address_Id", newEvent.AddressID);
                                command.Parameters.AddWithValue(@"Start_Time", newEvent.StartTime ?? default);
                                command.Parameters.AddWithValue(@"End_Time", newEvent.EndTime ?? default);
                                command.Parameters.AddWithValue(@"Link_Id", newEvent.LinkID);
                                command.Parameters.AddWithValue(@"ClosingRegistration", newEvent.ClosingRegistration ?? default);
                                command.Parameters.AddWithValue(@"IsRegistrationOpen", newEvent.IsRegistrationOpen ?? default);
                                command.Parameters.AddWithValue(@"IsOnline", newEvent.IsOnline ?? false);
                                command.Parameters.AddWithValue(@"Slug", newEvent.UrlSlug ?? string.Empty);
                                command.Parameters.AddWithValue(@"Updated", newEvent.LastUpdate);
                                var result = await command.ExecuteNonQueryAsync();
                                if (result > 0) totalSuccess++;
                                Console.WriteLine($"Updated Event: {newEvent.LinkID} {newEvent.EventName}");
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
    public async Task<int> InsertNewRegionData(RegionData newData)
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
}
