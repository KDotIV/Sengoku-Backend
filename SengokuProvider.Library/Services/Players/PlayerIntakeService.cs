using Dapper;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Worker.Handlers;
using System.Text;

namespace SengokuProvider.Library.Services.Players;

public sealed class PlayerIntakeService : IPlayerIntakeService
{
    private readonly string _connectionString;
    private readonly ICommonDatabaseService _commonDatabaseService;
    private readonly IEventQueryService _eventQueryService;
    private readonly IConfiguration _config;
    private readonly IAzureBusApiService _azureBusApiService;
    private static readonly Random _rand = new();

    public PlayerIntakeService(string connectionString, ICommonDatabaseService commonDatabaseService, IEventQueryService eventQueryService, IConfiguration config, IAzureBusApiService azureBusApiService)
    {
        _connectionString = connectionString;
        _commonDatabaseService = commonDatabaseService;
        _eventQueryService = eventQueryService;
        _config = config;
        _azureBusApiService = azureBusApiService;
    }
    public async Task<PlayerOnboardResult> SaveVictoryPathData(BracketVictoryPathData processedData)
        {
            if (processedData == null || processedData.EntrantSetCards == null || processedData.EntrantSetCards.Count == 0)
            {
                return new PlayerOnboardResult { Response = "FAILED: Cannot save invalid Bracket Data" };
            }

            var setResponse = await SaveTournamentSetData(processedData.EntrantSetCards);
            if (setResponse.Successful.Count == 0) { setResponse.Response = "No Sets were inserted. Can't Create Victory Path"; return setResponse; }

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                var setIds = _commonDatabaseService.CreateDBTextArrayType("@SetIds", setResponse.Successful.ToArray());
                var newPathId = await GenerateNewBracketPathId();
                try
                {
                    using (var cmd = new NpgsqlCommand(@"INSERT INTO bracket_paths (id, tournament_link, tournament_name, event_link, round_num, player_id, last_updated, set_ids) 
                                                        VALUES (@ID, @TournamentLink, @TournamentName, @EventLink, @RoundNum, @PlayerId, @LastUpdated, @SetIds) ON CONFLICT DO NOTHING;", conn))
                    {
                        cmd.Parameters.AddWithValue("@ID", newPathId);
                        cmd.Parameters.AddWithValue("@TournamentLink", processedData.TournamentLinkID);
                        cmd.Parameters.AddWithValue("@TournamentName", processedData.TournamentName);
                        cmd.Parameters.AddWithValue("@EventLink", processedData.EventLinkID);
                        cmd.Parameters.AddWithValue("@RoundNum", processedData.RoundNum);
                        cmd.Parameters.AddWithValue("@PlayerId", processedData.PlayerTournamentCard.PlayerID);
                        cmd.Parameters.AddWithValue("@LastUpdated", DateTime.UtcNow);
                        cmd.Parameters.Add(setIds);

                        var result = await cmd.ExecuteNonQueryAsync();

                        if (result > 0)
                        {
                            Console.WriteLine($"Victory Path Inserted for Player: {processedData.PlayerTournamentCard.PlayerID} in Tournament: {processedData.TournamentLinkID}");
                            setResponse.Response = $"Bracket Path Inserted Successfully: PlayerID: {processedData.PlayerTournamentCard.PlayerID} Tournament: {processedData.TournamentLinkID}";
                            return setResponse;
                        }
                        else
                        {
                            Console.WriteLine($"Victory Path already exists for Player: {processedData.PlayerTournamentCard.PlayerID} in Tournament: {processedData.TournamentLinkID}");
                            setResponse.Response = "Victory Path already exists";
                            return setResponse;
                        }
                    }
                }
                catch (NpgsqlException ex)
                {
                    throw new ApplicationException($"Database error occurred: {ex.StackTrace}", ex);
                }
                catch (Exception ex)
                {
                    throw new ApplicationException($"Error While Processing: {ex.Message} - {ex.StackTrace}");
                }
            }
        }
        private async Task<PlayerOnboardResult> SaveTournamentSetData(List<EntrantSetCard> entrantsData)
        {
            var onboardResult = new PlayerOnboardResult { Response = "Open" };
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var transaction = await conn.BeginTransactionAsync())
                {
                    foreach (var entrantCard in entrantsData)
                    {
                        if (entrantCard.PlayerOneID == 0 || entrantCard.PlayerTwoID == 0)
                        {
                            Console.WriteLine("Entrant Card is missing Player IDs. Cannot insert into database");
                            onboardResult.Failures.Add(entrantCard.SetID);
                            continue;
                        }
                        try
                        {
                            using (var cmd = new NpgsqlCommand(@"INSERT INTO tournament_sets (id, playerone_id, playerone_name, playertwo_id, playertwo_name, last_updated) VALUES (@SetId, @PlayerOneId, @PlayerOneName, @PlayerTwoId, @PlayerTwoName, @LastUpdated) ON CONFLICT DO NOTHING;", conn))
                            {
                                cmd.Parameters.AddWithValue("@SetId", entrantCard.SetID);
                                cmd.Parameters.AddWithValue("@PlayerOneId", entrantCard.PlayerOneID);
                                cmd.Parameters.AddWithValue("@PlayerOneName", entrantCard.EntrantOneName);
                                cmd.Parameters.AddWithValue("@PlayerTwoId", entrantCard.PlayerTwoID);
                                cmd.Parameters.AddWithValue("@PlayerTwoName", entrantCard.EntrantTwoName);
                                cmd.Parameters.AddWithValue("@LastUpdated", DateTime.UtcNow);

                                var result = await cmd.ExecuteNonQueryAsync();
                                if (result == 0)
                                {
                                    Console.WriteLine($"Set already exists in database for Players: {entrantCard.PlayerOneID} vs {entrantCard.PlayerTwoID}");
                                    onboardResult.Successful.Add(entrantCard.SetID);
                                }
                                if (result > 0)
                                {
                                    Console.WriteLine($"Set Inserted for Players: {entrantCard.PlayerOneID} vs {entrantCard.PlayerTwoID}");
                                    onboardResult.Successful.Add(entrantCard.SetID);
                                }
                            }
                        }
                        catch (NpgsqlException ex)
                        {
                            onboardResult.Response = ex.Message;
                            onboardResult.Failures.Add(entrantCard.SetID);
                            continue;
                        }
                        catch (Exception ex)
                        {
                            onboardResult.Response = ex.Message;
                            onboardResult.Failures.Add(entrantCard.SetID);
                            continue;
                        }
                    }
                    await transaction.CommitAsync();
                }
                await conn.CloseAsync();
            }
            return onboardResult;
        }
    public async Task<int> IntakePlayerStandingData(List<PlayerStandingResult> currentStandings)
        {
            if (currentStandings == null || currentStandings.Count == 0) return 0;

            int totalSuccess = 0;
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var transaction = await conn.BeginTransactionAsync())
                    {
                        var insertQuery = new StringBuilder(@"INSERT INTO standings (entrant_id, player_id, tournament_link, placement, entrants_num, active, gained_points, last_updated) VALUES ");
                        var queryParams = new List<NpgsqlParameter>();
                        var valueCount = 0;

                        var uniqueStandings = currentStandings
                            .GroupBy(s => s.TournamentLinks!.EntrantId)
                            .Select(g => g.OrderBy(x => x.StandingDetails.Placement)
                            .First())
                            .ToList();

                        foreach (var data in uniqueStandings)
                        {
                            var tempArrInput = new int[] { data.StandingDetails.TournamentId };
                            var checkTournamentLink = await _eventQueryService.GetTournamentLinksById(tempArrInput);
                            if (checkTournamentLink.Count == 0 || checkTournamentLink.FirstOrDefault()?.Id == 0)
                            {
                                Console.WriteLine($"TournamentLink does not exist for this Standing Data. Sending TournamentLink: {data.StandingDetails.TournamentId} to EventQueue");
                                await SendTournamentLinkEventMessage(data.StandingDetails.EventId);
                                continue;
                            }

                            if (data.TournamentLinks == null || data.TournamentLinks.PlayerId == 0)
                            {
                                Console.WriteLine("Standing Data is missing Player Startgg link. Can't link to player");
                                continue;
                            }

                            int exists = await VerifyPlayer(data.TournamentLinks.PlayerId);
                            if (exists == 0)
                            {
                                Console.WriteLine("Player does not exist. Sending request to intake player");
                                continue;
                            }
                            if (valueCount > 0)
                            {
                                insertQuery.Append(", ");
                            }
                            insertQuery.Append($"(@EntrantInput{valueCount}, @PlayerId{valueCount}, @TournamentLink{valueCount}, @PlacementInput{valueCount}, @NumEntrants{valueCount}, @IsActive{valueCount}, @NewPoints{valueCount}, @LastUpdated{valueCount})");

                            queryParams.Add(new NpgsqlParameter($"@EntrantInput{valueCount}", data.TournamentLinks.EntrantId));
                            queryParams.Add(new NpgsqlParameter($"@PlayerId{valueCount}", exists));
                            queryParams.Add(new NpgsqlParameter($"@TournamentLink{valueCount}", data.StandingDetails.TournamentId));
                            queryParams.Add(new NpgsqlParameter($"@PlacementInput{valueCount}", data.StandingDetails.Placement));
                            queryParams.Add(new NpgsqlParameter($"@NumEntrants{valueCount}", data.EntrantsNum));
                            queryParams.Add(new NpgsqlParameter($"@IsActive{valueCount}", data.StandingDetails.IsActive));
                            queryParams.Add(new NpgsqlParameter($"@NewPoints{valueCount}", data.StandingDetails.LeaguePoints));
                            queryParams.Add(new NpgsqlParameter($"@LastUpdated{valueCount}", data.LastUpdated));

                            valueCount++;
                        }
                        if (valueCount == 0)
                        {
                            Console.WriteLine("No valid standing records found for this batch.");
                            await transaction.CommitAsync();
                            return totalSuccess;
                        }

                        insertQuery.Append(" ON CONFLICT (entrant_id) DO UPDATE SET player_id = EXCLUDED.player_id, tournament_link = EXCLUDED.tournament_link, placement = EXCLUDED.placement, entrants_num = EXCLUDED.entrants_num, active = EXCLUDED.active, gained_points = EXCLUDED.gained_points, last_updated = EXCLUDED.last_updated;");

                        using (var cmd = new NpgsqlCommand(insertQuery.ToString(), conn))
                        {
                            cmd.Transaction = transaction;
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
        private async Task<int> VerifyPlayer(int playerId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new NpgsqlCommand(@"SELECT id FROM players WHERE startgg_link = @Input", conn))
                    {
                        cmd.Parameters.AddWithValue("@Input", playerId);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                return reader.GetInt32(reader.GetOrdinal("id"));
                            }
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
            return 0;
        }
    public async Task<int> InsertNewPlayerData(List<PlayerData> players)
        {
            try
            {
                int totalSuccess = 0;
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var transaction = await conn.BeginTransactionAsync())
                    {
                        foreach (var player in players)
                        {
                            var createInsertCommand = @"
                            INSERT INTO players (id, player_name, startgg_link, last_updated, user_link)
                            VALUES (@IdInput, @PlayerName, @PlayerLinkId, @LastUpdated, @UserLink)
                            ON CONFLICT (startgg_link) DO UPDATE SET
                                player_name = EXCLUDED.player_name,
                                startgg_link = EXCLUDED.startgg_link,
                                last_updated = EXCLUDED.last_updated,
                                user_link = EXCLUDED.user_link;";
                            using (var cmd = new NpgsqlCommand(createInsertCommand, conn))
                            {
                                cmd.Transaction = transaction;
                                cmd.Parameters.AddWithValue("@IdInput", player.Id);
                                cmd.Parameters.AddWithValue("@PlayerName", player.PlayerName);
                                cmd.Parameters.AddWithValue("@PlayerLinkId", player.PlayerLinkID);
                                cmd.Parameters.AddWithValue("@LastUpdated", player.LastUpdate);
                                cmd.Parameters.AddWithValue("@UserLink", player.UserLink);
                                int result = await cmd.ExecuteNonQueryAsync();
                                if (result > 0) { Console.WriteLine("Player Inserted"); totalSuccess += result; }
                            }
                        }
                        await transaction.CommitAsync();
                    }
                }
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
            return 0;
        }
        private async Task<int> GenerateNewBracketPathId()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                while (true)
                {
                    var newId = _rand.Next(100000, 1000000);
                    var newQuery = @"SELECT id FROM bracket_paths where id = @Input";
                    var queryResult = await conn.QueryFirstOrDefaultAsync<int>(newQuery, new { Input = newId });
                    if (newId != queryResult || queryResult == 0) return newId;
                }
            }
        }
        private async Task<bool> SendTournamentLinkEventMessage(int eventLinkId)
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
}
