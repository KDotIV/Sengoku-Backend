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
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            var result = await SaveVictoryPathData(processedData, connection, transaction);
            await transaction.CommitAsync();
            return result;
        }
        catch (NpgsqlException ex)
        {
            var message = $"Database error while committing bracket path for tournament {processedData?.TournamentLinkID} (SQLSTATE {ex.SqlState}): {ex.Message}";
            Console.Error.WriteLine($"{message}{Environment.NewLine}{ex}");
            throw new ApplicationException(message, ex);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error while committing bracket path for tournament {processedData?.TournamentLinkID}: {ex}");
            throw;
        }
    }

    public async Task<PlayerOnboardResult> SaveVictoryPathData(BracketVictoryPathData data,
        NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        try
        {
            if (data?.EntrantSetCards == null || data.EntrantSetCards.Count == 0 ||
                data.EntrantSetCards.Any(x => x.PlayerOneID <= 0 || x.PlayerTwoID <= 0 || string.IsNullOrWhiteSpace(x.SetID)))
                throw new ArgumentException("Cannot save incomplete bracket data.");
            var matchupKeys = data.EntrantSetCards.Select(x => x.SetID).Distinct().Order(StringComparer.Ordinal).ToArray();
            // Also protects callers outside the checkpoint workflow. A retry must reuse
            // the existing path rather than allocate another random primary key.
            var identity = $"{data.TournamentLinkID}:{data.PlayerTournamentCard.PlayerID}:{string.Join(',', matchupKeys)}";
            await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@identity, 1))", new { identity }, transaction);
            // Checkpoint SetID is a logical matchup key, not a database integer ID.
            // Allocate the mapping in the same transaction as the sets and path.
            await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(728349104)", transaction: transaction);
            var setIdSequence = await connection.ExecuteScalarAsync<string>(
                "SELECT COALESCE(pg_get_serial_sequence('tournament_sets', 'id'), 'bracket_matchup_set_id_seq')", transaction: transaction);
            var setIdsByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var key in matchupKeys)
            {
                var mappedId = await connection.QuerySingleOrDefaultAsync<int?>(
                    "SELECT set_id FROM bracket_matchup_keys WHERE matchup_key = @key", new { key }, transaction);
                if (mappedId == null)
                {
                    int candidate;
                    bool occupied;
                    do
                    {
                        candidate = await connection.ExecuteScalarAsync<int>("SELECT nextval(CAST(@setIdSequence AS regclass))", new { setIdSequence }, transaction);
                        // Existing sets may predate this mapping. Never overwrite one
                        // merely because the new sequence allocated its integer ID.
                        occupied = await connection.ExecuteScalarAsync<bool>("""
                            SELECT EXISTS(SELECT 1 FROM tournament_sets WHERE id::text = @idText)
                                OR EXISTS(SELECT 1 FROM bracket_matchup_keys WHERE set_id = @candidate)
                            """, new { candidate, idText = candidate.ToString(System.Globalization.CultureInfo.InvariantCulture) }, transaction);
                    } while (occupied);
                    await connection.ExecuteAsync("INSERT INTO bracket_matchup_keys (matchup_key, set_id) VALUES (@key, @candidate)",
                        new { key, candidate }, transaction);
                    mappedId = candidate;
                }
                setIdsByKey.Add(key, mappedId.Value);
            }
            var setIds = setIdsByKey.Values.Order().ToArray(); // Npgsql binds this as integer[].
            var existing = await connection.QuerySingleOrDefaultAsync<int?>("""
                SELECT id FROM bracket_paths
                WHERE tournament_link = @TournamentLinkID AND player_id = @PlayerID
                    AND set_ids @> @setIds AND set_ids <@ @setIds
                ORDER BY id LIMIT 1
                """, new { data.TournamentLinkID, data.PlayerTournamentCard.PlayerID, setIds }, transaction);
            foreach (var card in data.EntrantSetCards.DistinctBy(x => x.SetID))
                await connection.ExecuteAsync("""
                    INSERT INTO tournament_sets (id, playerone_id, playerone_name, playertwo_id, playertwo_name, last_updated)
                    OVERRIDING SYSTEM VALUE
                    VALUES (@SetID, @PlayerOneID, @EntrantOneName, @PlayerTwoID, @EntrantTwoName, now())
                    ON CONFLICT (id) DO UPDATE SET playerone_id = EXCLUDED.playerone_id,
                        playerone_name = EXCLUDED.playerone_name, playertwo_id = EXCLUDED.playertwo_id,
                        playertwo_name = EXCLUDED.playertwo_name, last_updated = now()
                    """, new { SetID = setIdsByKey[card.SetID], card.PlayerOneID, card.EntrantOneName,
                        card.PlayerTwoID, card.EntrantTwoName }, transaction);
            if (existing == null)
            {
                // Serialize random-ID allocation too, avoiding a collision between
                // concurrent saves for different bracket requests.
                await connection.ExecuteAsync("SELECT pg_advisory_xact_lock(728349102)", transaction: transaction);
                int pathId;
                do { pathId = _rand.Next(100000, 1000000); }
                while (await connection.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM bracket_paths WHERE id = @pathId)", new { pathId }, transaction));
                await connection.ExecuteAsync("""
                    INSERT INTO bracket_paths (id, tournament_link, tournament_name, event_link, round_num, player_id, last_updated, set_ids)
                    VALUES (@pathId, @TournamentLinkID, @TournamentName, @EventLinkID, @RoundNum, @PlayerID, now(), @setIds)
                    """, new { pathId, data.TournamentLinkID, data.TournamentName, data.EventLinkID, data.RoundNum,
                        data.PlayerTournamentCard.PlayerID, setIds }, transaction);
            }
            return new PlayerOnboardResult { Response = existing == null ? "Bracket path saved successfully" : "Bracket path already exists",
                Status = "Completed", Successful = setIds.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList() };
        }
        catch (NpgsqlException ex)
        {
            var message = $"Database error while saving bracket path for tournament {data?.TournamentLinkID}, player {data?.PlayerTournamentCard?.PlayerID} (SQLSTATE {ex.SqlState}): {ex.Message}";
            Console.Error.WriteLine($"{message}{Environment.NewLine}{ex}");
            throw new ApplicationException(message, ex);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error while saving bracket path for tournament {data?.TournamentLinkID}, player {data?.PlayerTournamentCard?.PlayerID}: {ex}");
            throw;
        }
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
