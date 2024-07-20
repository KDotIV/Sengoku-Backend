using Dapper;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Npgsql;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Legends;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Library.Services.Legends;
using SengokuProvider.Worker.Handlers;
using System.Collections.Concurrent;

namespace SengokuProvider.Library.Services.Players
{
    public class PlayerIntakeService : IPlayerIntakeService
    {
        private readonly IPlayerQueryService _queryService;
        private readonly ILegendQueryService _legendQueryService;
        private readonly IAzureBusApiService _azureBusApiService;
        private readonly IConfiguration _config;

        private readonly string _connectionString;
        private ConcurrentDictionary<int, int> _playersCache;
        private ConcurrentDictionary<int, string> _playerRegistry;
        private HashSet<int> _eventCache;
        private int _currentEventId;
        private static Random _rand = new Random();

        public PlayerIntakeService(string connectionString, IConfiguration configuration, IPlayerQueryService playerQueryService,
            ILegendQueryService legendQueryService, IAzureBusApiService serviceBus)
        {
            _connectionString = connectionString;
            _config = configuration;
            _queryService = playerQueryService;
            _legendQueryService = legendQueryService;
            _azureBusApiService = serviceBus;
            _playersCache = new ConcurrentDictionary<int, int>();
            _playerRegistry = new ConcurrentDictionary<int, string>();
            _eventCache = new HashSet<int>();
        }
        public async Task<bool> SendPlayerIntakeMessage(string eventSlug, int perPage = 50, int pageNum = 1)
        {
            if (string.IsNullOrEmpty(_config["ServiceBusSettings:PlayerReceivedQueue"]) || _config == null)
            {
                Console.WriteLine("Service Bus Settings Cannot be empty or null");
                return false;
            }
            if (string.IsNullOrEmpty(eventSlug))
            {
                Console.WriteLine("Event Url cannot be null or empty");
                return false;
            }

            try
            {
                var newCommand = new PlayerReceivedData
                {
                    Command = new IntakePlayersByTournamentCommand
                    {
                        Topic = CommandRegistry.IntakePlayersByTournament,
                        EventSlug = eventSlug,
                        PerPage = perPage,
                        PageNum = pageNum
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
        public async Task<int> IntakePlayerData(IntakePlayersByTournamentCommand command)
        {
            try
            {
                PlayerGraphQLResult? newPlayerData = await _queryService.GetPlayerDataFromStartgg(command);
                if (newPlayerData == null) { return 0; }

                _eventCache.Add(newPlayerData.Data.Id);
                _currentEventId = newPlayerData.Data.Id;
                int playerSuccess = await ProcessPlayerData(newPlayerData);

                Console.WriteLine($"Players Inserted from Registry: {_playerRegistry.Count}");

                Console.WriteLine("Starting Standings Processing");
                var standingsSuccess = await ProcessNewPlayers();

                Console.WriteLine($"{standingsSuccess} total standings added for player");

                return playerSuccess;
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred during Player Intake: {ex.StackTrace}", ex);
            }
        }
        public async Task<int> OnboardPreviousTournamentData(OnboardPlayerDataCommand command, int volumeLimit = 100)
        {
            List<Task<int>> batchTasks = new List<Task<int>>();
            List<PlayerStandingResult> currentBatch = new List<PlayerStandingResult>();
            try
            {
                PastEventPlayerData? queryResult = await _queryService.QueryStartggPreviousEventData(command);

                if (queryResult == null || queryResult.PlayerQuery == null || queryResult.PlayerQuery.User.PreviousEvents.Nodes.Count == 0) { return 0; }

                List<PreviousNodes>? eventData = queryResult.PlayerQuery.User.PreviousEvents.Nodes;
                var standingsSuccess = await ProcessPreviousTournamentData(command, volumeLimit, batchTasks, currentBatch, eventData);
                Console.WriteLine($"{standingsSuccess} total standings added for player");

                return standingsSuccess;
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred during Player Intake: {ex.StackTrace}", ex);
            }
        }

        private async Task<int> ProcessPreviousTournamentData(OnboardPlayerDataCommand command, int volumeLimit, List<Task<int>> batchTasks, List<PlayerStandingResult> currentBatch, List<PreviousNodes> eventData)
        {
            foreach (var node in eventData)
            {
                Console.WriteLine("Querying Standings Data");
                PlayerStandingResult? newStanding = await _queryService.QueryPlayerStandings(new GetPlayerStandingsCommand { EventId = node.Id, GamerTag = command.GamerTag, PerPage = 20 });

                if (newStanding == null || newStanding.Response.Contains("Failed")) { continue; }

                newStanding.TournamentLinks.PlayerId = command.PlayerId;
                currentBatch.Add(newStanding);
                Console.WriteLine("Player Standing Data added");

                // Check if the current batch has reached the batch size
                if (currentBatch.Count >= volumeLimit)
                {
                    Console.WriteLine("Intaking Standings Batch");
                    // Process the current batch asynchronously
                    batchTasks.Add(IntakePlayerStandingData(new List<PlayerStandingResult>(currentBatch)));
                    currentBatch.Clear();
                    Console.WriteLine($"Batch cleared: {currentBatch.Count}");
                }
            }
            // Process any remaining entries in the final batch
            if (currentBatch.Count > 0)
            {
                batchTasks.Add(IntakePlayerStandingData(currentBatch));
            }

            // Await all batch tasks to complete
            var results = await Task.WhenAll(batchTasks);
            return results.Sum();
        }

        private async Task<int> ProcessNewPlayers(int volumeLimit = 100)
        {
            List<Task<int>> batchTasks = new List<Task<int>>();
            List<PlayerStandingResult> currentBatch = new List<PlayerStandingResult>();

            // Process each player in the registry
            foreach (var newPlayer in _playerRegistry)
            {
                Console.WriteLine("Querying Standings Data");
                PlayerStandingResult? newStanding = await _queryService.QueryPlayerStandings(new GetPlayerStandingsCommand { EventId = _currentEventId, GamerTag = newPlayer.Value, PerPage = 20 });
                if (newStanding == null || newStanding.Response.Contains("Failed")) { continue; }

                newStanding.TournamentLinks.PlayerId = newPlayer.Key;
                currentBatch.Add(newStanding);
                Console.WriteLine("Player Standing Data added");

                // Check if the current batch has reached the batch size
                if (currentBatch.Count >= volumeLimit)
                {
                    Console.WriteLine("Intaking Standings Batch");
                    // Process the current batch asynchronously
                    batchTasks.Add(IntakePlayerStandingData(new List<PlayerStandingResult>(currentBatch)));
                    currentBatch.Clear();
                    Console.WriteLine($"Batch cleared: {currentBatch.Count}");
                }
            }

            // Process any remaining entries in the final batch
            if (currentBatch.Count > 0)
            {
                batchTasks.Add(IntakePlayerStandingData(currentBatch));
            }

            // Await all batch tasks to complete
            var results = await Task.WhenAll(batchTasks);
            return results.Sum();
        }
        private async Task<int> IntakePlayerStandingData(List<PlayerStandingResult> currentStandings)
        {
            try
            {
                var totalSuccess = 0;
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var transaction = await conn.BeginTransactionAsync())
                    {
                        foreach (var data in currentStandings)
                        {
                            int exists = await VerifyPlayer(data.TournamentLinks.PlayerId);
                            if (exists == 0) { continue; }

                            var createInsertCommand = @"
                            INSERT INTO standings (entrant_id, player_id, tournament_link, placement, entrants_num, active, last_updated)
                            VALUES (@EntrantInput, @PlayerId, @TournamentLink, @PlacementInput, @NumEntrants, @IsActive, @LastUpdated)
                            ON CONFLICT (entrant_id) DO UPDATE SET
                                player_id = EXCLUDED.player_id,
                                tournament_link = EXCLUDED.tournament_link,
                                placement = EXCLUDED.placement,
                                entrants_num = EXCLUDED.entrants_num,
                                active = EXCLUDED.active;";

                            using (var cmd = new NpgsqlCommand(createInsertCommand, conn))
                            {
                                cmd.Transaction = transaction;
                                cmd.Parameters.AddWithValue("@EntrantInput", data.TournamentLinks.EntrantId);
                                cmd.Parameters.AddWithValue("@PlayerId", data.TournamentLinks.PlayerId);
                                cmd.Parameters.AddWithValue("@TournamentLink", data.StandingDetails.TournamentId);
                                cmd.Parameters.AddWithValue("@PlacementInput", data.StandingDetails.Placement);
                                cmd.Parameters.AddWithValue("@NumEntrants", data.EntrantsNum);
                                cmd.Parameters.AddWithValue("@IsActive", data.StandingDetails.IsActive);
                                cmd.Parameters.AddWithValue("@LastUpdated", data.LastUpdated);

                                int result = await cmd.ExecuteNonQueryAsync();
                                if (result > 0)
                                {
                                    await SendOnboardMessage(data.TournamentLinks.PlayerId, data.StandingDetails.GamerTag);
                                    result += totalSuccess;
                                    Console.WriteLine("Player removed from registry");
                                }
                            }
                        }
                        await transaction.CommitAsync();
                    }
                    return totalSuccess;
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
        private async Task SendOnboardMessage(int playerId, string playerName)
        {
            if (string.IsNullOrEmpty(_config["ServiceBusSettings:legendreceivedqueue"]) || _config == null)
            {
                Console.WriteLine("Service Bus Settings Cannot be empty or null");
                return;
            }
            try
            {
                var newCommand = new OnboardReceivedData
                {
                    Command = new OnboardLegendsByPlayerCommand
                    {
                        PlayerId = playerId,
                        GamerTag = playerName,
                        Topic = CommandRegistry.OnboardLegendsByPlayerData,
                    },
                    MessagePriority = MessagePriority.SystemIntake
                };
                var messageJson = JsonConvert.SerializeObject(newCommand, JsonSettings.DefaultSettings);
                var result = await _azureBusApiService.SendBatchAsync(_config["ServiceBusSettings:legendreceivedqueue"], messageJson);

                if (!result)
                {
                    Console.WriteLine("Failed to Send Onboarding Message to Service Bus. Check Data");
                    return;
                }
                _playerRegistry.TryRemove(playerId, out _);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected Error Sending Onboarding Message {ex.Message} {ex.StackTrace}");
            }
        }
        private async Task<int> VerifyPlayer(int playerId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new NpgsqlCommand(@"SELECT id FROM players WHERE id = @Input", conn))
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
        private async Task<int> ProcessPlayerData(PlayerGraphQLResult queryData)
        {
            var players = new List<PlayerData>();
            if (queryData.Data == null) throw new ApplicationException("Player Query Data was null from Start.gg");
            foreach (var node in queryData.Data.Entrants.Nodes)
            {
                var firstRecord = node.Participants.FirstOrDefault();
                if (firstRecord == null) continue;
                if (!_playersCache.TryGetValue(firstRecord.Player.Id, out int databaseId))
                {
                    databaseId = await CheckDuplicatePlayer(firstRecord);
                    if (databaseId == 0)
                    {
                        var newPlayerData = new PlayerData
                        {
                            Id = await GenerateNewPlayerId(),
                            PlayerName = firstRecord.Player.GamerTag,
                            PlayerLinkID = firstRecord.Player.Id,
                            LastUpdate = DateTime.UtcNow,
                        };
                        players.Add(newPlayerData);
                        databaseId = newPlayerData.Id;
                    }
                }
                _playerRegistry.TryAdd(databaseId, firstRecord.Player.GamerTag);
            }
            return await InsertNewPlayerData(players);
        }
        private async Task<int> InsertNewPlayerData(List<PlayerData> players)
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
                            INSERT INTO players (id, player_name, startgg_link, last_updated)
                            VALUES (@IdInput, @PlayerName, @PlayerLinkId, @LastUpdated)
                            ON CONFLICT (id) DO UPDATE SET
                                player_name = EXCLUDED.player_name,
                                startgg_link = EXCLUDED.startgg_link;";
                            using (var cmd = new NpgsqlCommand(createInsertCommand, conn))
                            {
                                cmd.Transaction = transaction;
                                cmd.Parameters.AddWithValue("@IdInput", player.Id);
                                cmd.Parameters.AddWithValue("@PlayerName", player.PlayerName);
                                cmd.Parameters.AddWithValue("@PlayerLinkId", player.PlayerLinkID);
                                cmd.Parameters.AddWithValue("@LastUpdated", player.LastUpdate);

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
        private async Task<int> GenerateNewPlayerId()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                while (true)
                {
                    var newId = _rand.Next(100000, 1000000);

                    var newQuery = @"SELECT id FROM players where id = @Input";
                    var queryResult = await conn.QueryFirstOrDefaultAsync<int>(newQuery, new { Input = newId });
                    if (newId != queryResult || queryResult == 0) return newId;
                }
            }
        }
        private async Task<int> CheckDuplicatePlayer(Participant participantRecord)
        {
            try
            {

                if (_playersCache.TryGetValue(participantRecord.Player.Id, out int databaseId) && databaseId != 0) return databaseId;
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var newQuery = @"SELECT id FROM players WHERE startgg_link = @Input";
                    databaseId = await conn.QueryFirstOrDefaultAsync<int>(newQuery, new { Input = participantRecord.Player.Id });
                    if (databaseId != 0) _playersCache.TryAdd(participantRecord.Player.Id, databaseId);

                    return databaseId;
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
    }
}
