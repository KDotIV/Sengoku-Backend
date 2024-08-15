using Dapper;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Npgsql;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Legends;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Worker.Handlers;

namespace SengokuProvider.Library.Services.Legends
{
    public class LegendIntakeService : ILegendIntakeService
    {
        private readonly IConfiguration _configuration;
        private readonly ILegendQueryService _legendQueryService;
        private readonly IAzureBusApiService _azureBusApiService;
        private readonly string _connectionString;
        private static Random _rand = new Random();
        public LegendIntakeService(string connectionString, IConfiguration configuration, ILegendQueryService queryService, IAzureBusApiService azureServiceBus)
        {
            _configuration = configuration;
            _connectionString = connectionString;
            _legendQueryService = queryService;
            _azureBusApiService = azureServiceBus;
        }

        public async Task<LegendData?> GenerateNewLegends(int playerId, string playerName)
        {
            Console.WriteLine("Beginning Onboarding Process...");

            try
            {
                var currentData = await _legendQueryService.QueryStandingsByPlayerId(playerId);

                LegendData? newLegend = await BuildLegendData(currentData, playerName);

                return newLegend;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error While Generating New Legend for PlayerID: {playerId} - {ex.Message}", ex.StackTrace);
                Console.WriteLine($"Sending Player for Onboarding");
                if (await SendPlayerIntakeMessage(playerId, playerName)) { Console.WriteLine("Successfully Sent Player Onbaord Message"); }
            }

            return null;
        }
        public async Task<TournamentOnboardResult> AddTournamentToLeague(int[] tournamentIds, int leagueId)
        {
            var newOnboardResult = new TournamentOnboardResult { Response = "Open" };

            if (leagueId < 0) { newOnboardResult.Response = "LeagueId cannot be invalid ids"; }

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var transaction = await conn.BeginTransactionAsync())
                    {
                        foreach (var tournamentId in tournamentIds)
                        {
                            using (var cmd = new NpgsqlCommand(@"INSERT INTO tournament_leagues (tournament_id, league_id, last_updated) VALUES (@TournamentInput, @LeagueInput, @LastUpdated) ON CONFLICT DO NOTHING;", conn))
                            {
                                cmd.Parameters.AddWithValue("@TournamentInput", tournamentId);
                                cmd.Parameters.AddWithValue("@LeagueInput", leagueId);
                                cmd.Parameters.AddWithValue("@LastUpdated", DateTime.UtcNow);

                                var result = await cmd.ExecuteNonQueryAsync();
                                if (result > 0)
                                {
                                    newOnboardResult.Response = "Successfully Inserted Tournament to League";
                                    newOnboardResult.Successful.Add(tournamentId);
                                }
                                else { newOnboardResult.Failures.Add(tournamentId); }
                            }
                        }
                        await transaction.CommitAsync();
                    }
                }
                return newOnboardResult;
            }
            catch (NpgsqlException ex)
            {
                newOnboardResult.Response = ex.Message;
                throw new ApplicationException("Database error occurred: ", ex);
            }
            catch (Exception ex)
            {
                newOnboardResult.Response = ex.Message;
                throw new ApplicationException("Unexpected Error Occurred: ", ex);
            }
        }
        public async Task<PlayerOnboardResult> AddPlayerToLeague(int[] playerIds, int leagueId)
        {
            var newOnboardResult = new PlayerOnboardResult { Response = "Open" };

            if (leagueId < 0) { newOnboardResult.Response = "PlayerId or LeagueId cannot be invalid ids"; }

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var transaction = await conn.BeginTransactionAsync())
                    {
                        foreach (var playerId in playerIds)
                        {
                            using (var cmd = new NpgsqlCommand(@"INSERT INTO player_leagues (player_id, league_id, last_updated) VALUES (@PlayerInput, @LeagueInput, @LastUpdated) ON CONFLICT DO NOTHING;", conn))
                            {
                                cmd.Parameters.AddWithValue("@PlayerInput", playerId);
                                cmd.Parameters.AddWithValue("@LeagueInput", leagueId);
                                cmd.Parameters.AddWithValue("@LastUpdated", DateTime.UtcNow);

                                var result = await cmd.ExecuteNonQueryAsync();
                                if (result > 0)
                                {
                                    newOnboardResult.Response = "Successfully Inserted Tournament to League";
                                    newOnboardResult.Successful.Add(playerId);
                                }
                                else { newOnboardResult.Failures.Add(playerId); }
                            }
                        }
                        await transaction.CommitAsync();
                    }
                }
                return newOnboardResult;
            }
            catch (NpgsqlException ex)
            {
                newOnboardResult.Response = ex.Message;
                throw new ApplicationException("Database error occurred: ", ex);
            }
            catch (Exception ex)
            {
                newOnboardResult.Response = ex.Message;
                throw new ApplicationException("Unexpected Error Occurred: ", ex);
            }
            return newOnboardResult;
        }
        public async Task<int> InsertNewLegendData(LegendData newLegend)
        {
            if (newLegend == null) { throw new ArgumentNullException(nameof(newLegend)); }
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"INSERT INTO legends (id, legend_name, player_name, player_id, player_link_id, standings, last_updated) 
                    VALUES (@Id, @LegendName, @PlayerName, @PlayerId, @PlayerLink, @Standings, @LastUpdated)
                    ON CONFLICT (id) DO NOTHING RETURNING id", conn))
                    {
                        cmd.Parameters.AddWithValue("@Id", newLegend.Id);
                        cmd.Parameters.AddWithValue("@LegendName", newLegend.LegendName);
                        cmd.Parameters.AddWithValue("@PlayerName", newLegend.PlayerName);
                        cmd.Parameters.AddWithValue("@PlayerId", newLegend.PlayerId);
                        cmd.Parameters.AddWithValue("@PlayerLink", newLegend.PlayerLinkId);
                        if (newLegend.Standings != null && newLegend.Standings.Count > 0)
                        {
                            cmd.Parameters.Add(CreateDBIntArrayType("@Standings", newLegend.Standings.ToArray()));
                        }
                        else { cmd.Parameters.Add(CreateDBIntArrayType("@Standings", Array.Empty<int>())); }
                        cmd.Parameters.AddWithValue("@LastUpdated", DateTime.UtcNow);

                        var result = await cmd.ExecuteScalarAsync();
                        return result != null ? Convert.ToInt32(result) : 0;
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
        }
        private async Task<LegendData?> BuildLegendData(StandingsQueryResult? currentData, string playerName)
        {
            if (currentData == null)
            {
                throw new ArgumentNullException(nameof(currentData), "Player Data cannot be null.");
            }

            // Check for existing legend
            int existingLegendId = await CheckDuplicateLegend(currentData.PlayerID);
            if (existingLegendId > 0)
            {
                throw new ApplicationException($"Legend already exists for Player: {currentData.PlayerID}");
            }

            // Create or use existing ID based on whether the legend was found
            int legendId = existingLegendId > 0 ? existingLegendId : await GenerateNewLegendId();

            LegendData legendData = new LegendData
            {
                Id = legendId,
                LegendName = "Placeholder Style",
                PlayerId = currentData.PlayerID,
                PlayerLinkId = 0,
                PlayerName = playerName,
                Standings = currentData.StandingData?.Select(standing => standing.Placement).ToList() ?? new List<int>()
            };

            return legendData;
        }
        private async Task<int> CheckDuplicateLegend(int playerId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var newQuery = @"SELECT id FROM legends WHERE player_id = @Input";
                    var databaseResult = await conn.QueryFirstOrDefaultAsync<int>(newQuery, new { Input = playerId });
                    return databaseResult;
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
        private async Task<bool> SendPlayerIntakeMessage(int playerId, string gamerTag)
        {
            if (string.IsNullOrEmpty(_configuration["ServiceBusSettings:PlayerReceivedQueue"]) || _configuration == null)
            {
                Console.WriteLine("Service Bus Settings Cannot be empty or null");
                return false;
            }
            if (string.IsNullOrEmpty(gamerTag) || playerId == 0)
            {
                Console.WriteLine("Player Intake Data cannot be null or empty");
                return false;
            }

            try
            {
                var newCommand = new PlayerReceivedData
                {
                    Command = new OnboardPlayerDataCommand
                    {
                        Topic = CommandRegistry.OnboardPlayerData,
                        PlayerId = playerId,
                        GamerTag = gamerTag
                    },
                    MessagePriority = MessagePriority.SystemIntake
                };
                var messageJson = JsonConvert.SerializeObject(newCommand, JsonSettings.DefaultSettings);
                var result = await _azureBusApiService.SendBatchAsync(_configuration["ServiceBusSettings:PlayerReceivedQueue"], messageJson);
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
        private async Task<int> GenerateNewLegendId()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                while (true)
                {
                    var newId = _rand.Next(100000, 1000000);

                    var newQuery = @"SELECT id FROM legends where id = @Input";
                    var queryResult = await conn.QueryFirstOrDefaultAsync<int>(newQuery, new { Input = newId });
                    if (newId != queryResult || queryResult == 0) return newId;
                }
            }
        }
        private NpgsqlParameter CreateDBIntArrayType(string parameterName, int[] array)
        {
            var newParameters = new NpgsqlParameter();
            newParameters.ParameterName = parameterName;
            newParameters.Value = array ?? Array.Empty<int>();
            newParameters.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer;

            return newParameters;
        }
    }
}