using Dapper;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Npgsql;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Legends;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Services.Legends;
using SengokuProvider.Worker.Handlers;
using System.Collections.Concurrent;

namespace SengokuProvider.Library.Services.Players
{
    public class PlayerIntakeService : IPlayerIntakeService
    {
        private readonly IPlayerQueryService _queryService;
        private readonly ILegendQueryService _legendQueryService;
        private readonly IEventQueryService _eventQueryService;
        private readonly IAzureBusApiService _azureBusApiService;
        private readonly IConfiguration _config;

        private readonly string _connectionString;
        private ConcurrentDictionary<int, int> _playersCache;
        private ConcurrentDictionary<int, string> _playerRegistry;
        private HashSet<int> _eventCache;
        private int _currentEventId;
        private static Random _rand = new Random();

        public PlayerIntakeService(string connectionString, IConfiguration configuration, IPlayerQueryService playerQueryService,
            ILegendQueryService legendQueryService, IEventQueryService eventQueryService, IAzureBusApiService serviceBus)
        {
            _connectionString = connectionString;
            _config = configuration;
            _queryService = playerQueryService;
            _legendQueryService = legendQueryService;
            _eventQueryService = eventQueryService;
            _azureBusApiService = serviceBus;
            _playersCache = new ConcurrentDictionary<int, int>();
            _playerRegistry = new ConcurrentDictionary<int, string>();
            _eventCache = new HashSet<int>();
        }
        public async Task<bool> SendPlayerIntakeMessage(int tournamentLink)
        {
            if (string.IsNullOrEmpty(_config["ServiceBusSettings:PlayerReceivedQueue"]) || _config == null)
            {
                Console.WriteLine("Service Bus Settings Cannot be empty or null");
                return false;
            }
            if (tournamentLink == 0)
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
        public async Task<int> IntakePlayerData(IntakePlayersByTournamentCommand command)
        {
            try
            {
                PlayerGraphQLResult? newPlayerData = await _queryService.QueryPlayerDataFromStartgg(command);
                if (newPlayerData == null) { return 0; }

                _eventCache.Add(newPlayerData.TournamentLink.EventLink.Id);
                _currentEventId = newPlayerData.TournamentLink.EventLink.Id;
                int playerSuccess = await ProcessPlayerData(newPlayerData);

                Console.WriteLine($"Players Inserted from Registry: {_playerRegistry.Count}");

                Console.WriteLine("Starting Standings Processing");
                var standingsSuccess = await ProcessNewPlayerStandings(newPlayerData);

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
                PastEventPlayerData queryResult = await _queryService.QueryStartggPreviousEventData(command.PlayerId, command.GamerTag, command.PerPage);

                if (queryResult == null || queryResult.PlayerQuery == null || queryResult.PlayerQuery.User == null || queryResult.PlayerQuery.User.Events == null || queryResult?.PlayerQuery?.User?.Events?.Nodes?.Count == 0) { return 0; }

                var mappedResult = MapPreviousTournamentData(queryResult);
                var standingsSuccess = await IntakePlayerStandingData(mappedResult);

                Console.WriteLine($"{standingsSuccess} total standings added for player");

                return standingsSuccess;
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred during Player Intake: {ex.StackTrace}", ex);
            }
        }
        private List<PlayerStandingResult> MapPreviousTournamentData(PastEventPlayerData? playerData)
        {
            List<PlayerStandingResult> mappedResult = new List<PlayerStandingResult>();

            if (playerData == null || playerData.PlayerQuery?.User?.Events?.Nodes == null || playerData.PlayerQuery.User.Events.Nodes.Count == 0)
            {
                Console.WriteLine("No PastPlayerData to process");
                return mappedResult;
            }

            const int participationPoints = 5;
            const int winnerBonus = 50;

            foreach (var tempNode in playerData.PlayerQuery.User.Events.Nodes)
            {
                if (tempNode == null || tempNode.Entrants?.Nodes == null || tempNode.Entrants.Nodes.Count == 0 || tempNode.NumEntrants == 0)
                {
                    continue;
                }

                var firstRecord = tempNode.Entrants.Nodes.First();
                if (firstRecord.Standing == null)
                {
                    continue;
                }

                int numEntrants = tempNode.NumEntrants ?? 0;
                int totalPoints = CalculateLeaguePoints(firstRecord, numEntrants);

                var newStanding = new PlayerStandingResult
                {
                    Response = "Open",
                    EntrantsNum = numEntrants,
                    UrlSlug = tempNode.Slug ?? string.Empty,
                    LastUpdated = DateTime.UtcNow,
                    StandingDetails = new StandingDetails
                    {
                        IsActive = firstRecord.Standing.IsActive,
                        Placement = firstRecord.Standing.Placement ?? 0,
                        GamerTag = playerData.PlayerQuery.GamerTag ?? string.Empty,
                        EventId = tempNode.EventLink?.Id ?? 0,
                        EventName = tempNode.EventLink?.Name ?? string.Empty,
                        TournamentId = tempNode.Id,
                        TournamentName = tempNode.Name ?? string.Empty
                    },
                    TournamentLinks = new Links
                    {
                        EntrantId = firstRecord.Id,
                        StandingId = firstRecord.Standing.Id,
                        PlayerId = firstRecord?.Participants?.FirstOrDefault()?.Player?.Id ?? 0
                    }
                };
                mappedResult.Add(newStanding);
            }
            return mappedResult;
        }
        private async Task<int> ProcessNewPlayerStandings(PlayerGraphQLResult tournamentData, int volumeLimit = 100)
        {
            var mappedStandings = MapStandingsData(tournamentData);
            var result = await IntakePlayerStandingData(mappedStandings);
            return result;
        }
        private List<PlayerStandingResult> MapStandingsData(PlayerGraphQLResult? data)
        {
            List<PlayerStandingResult> mappedResult = new List<PlayerStandingResult>();
            if (data == null) return mappedResult;

            foreach (var tempNode in data.TournamentLink.Entrants.Nodes)
            {
                if (tempNode.Standing == null) continue;
                int numEntrants = data.TournamentLink.NumEntrants ?? 0;
                try
                {
                    int totalPoints = CalculateLeaguePoints(tempNode, numEntrants);
                    var newStandings = new PlayerStandingResult
                    {
                        Response = "Open",
                        EntrantsNum = numEntrants,
                        LastUpdated = DateTime.UtcNow,
                        UrlSlug = data.TournamentLink.Slug,
                        StandingDetails = new StandingDetails
                        {
                            IsActive = tempNode.Standing.IsActive,
                            Placement = tempNode.Standing.Placement ?? 0,
                            GamerTag = tempNode.Participants?.FirstOrDefault()?.Player?.GamerTag ?? "",
                            EventId = data.TournamentLink.EventLink.Id,
                            EventName = data.TournamentLink.EventLink.Name,
                            TournamentId = data.TournamentLink.Id,
                            TournamentName = data.TournamentLink.Name,
                            LeaguePoints = totalPoints
                        },
                        TournamentLinks = new Links
                        {
                            EntrantId = tempNode.Id,
                            StandingId = tempNode.Standing.Id,
                            PlayerId = tempNode.Participants?.FirstOrDefault()?.Player?.Id ?? 0,
                        }
                    };
                    mappedResult.Add(newStandings);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error Occured populating Standing Data: {ex.Message}, {ex.StackTrace}");
                    continue;
                }
            }
            return mappedResult;
        }
        private int CalculateLeaguePoints(CommonEntrantNode tempNode, int totalEntrants)
        {
            int participationPoints = 5;
            int totalPoints = participationPoints;
            int placement = tempNode.Standing?.Placement ?? int.MaxValue;  // Default to MaxValue if placement is not found

            // Apply points based on placement
            foreach (var entry in CommonConstants.EnhancedPointDistribution)
            {
                if (placement <= entry.Key)
                {
                    totalPoints += entry.Value;
                    break;
                }
            }

            // Ensure minimum points for participation
            if (totalPoints < participationPoints)
            {
                totalPoints = participationPoints;
            }

            return totalPoints;
        }

        private async Task<int> IntakePlayerStandingData(List<PlayerStandingResult> currentStandings)
        {
            if (currentStandings == null || currentStandings.Count == 0) return 0;
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
                            var checkTournamentLink = await _eventQueryService.GetTournamentLinkById(data.StandingDetails.TournamentId);
                            if (checkTournamentLink.Id == 0)
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
                            if (exists == 0) { Console.WriteLine("Player does not exist. Sending request to intake player"); continue; }

                            var createInsertCommand = @"
                            INSERT INTO standings (entrant_id, player_id, tournament_link, placement, entrants_num, active, gained_points, last_updated)
                            VALUES (@EntrantInput, @PlayerId, @TournamentLink, @PlacementInput, @NumEntrants, @IsActive, @NewPoints, @LastUpdated)
                            ON CONFLICT (entrant_id) DO UPDATE SET
                                player_id = EXCLUDED.player_id,
                                tournament_link = EXCLUDED.tournament_link,
                                placement = EXCLUDED.placement,
                                entrants_num = EXCLUDED.entrants_num,
                                active = EXCLUDED.active,
                                gained_points = EXCLUDED.gained_points;";

                            using (var cmd = new NpgsqlCommand(createInsertCommand, conn))
                            {
                                cmd.Transaction = transaction;
                                cmd.Parameters.AddWithValue("@EntrantInput", data.TournamentLinks.EntrantId);
                                cmd.Parameters.AddWithValue("@PlayerId", exists);
                                cmd.Parameters.AddWithValue("@TournamentLink", data.StandingDetails.TournamentId);
                                cmd.Parameters.AddWithValue("@PlacementInput", data.StandingDetails.Placement);
                                cmd.Parameters.AddWithValue("@NumEntrants", data.EntrantsNum);
                                cmd.Parameters.AddWithValue("@IsActive", data.StandingDetails.IsActive);
                                cmd.Parameters.AddWithValue("@NewPoints", data.StandingDetails.LeaguePoints);
                                cmd.Parameters.AddWithValue("@LastUpdated", data.LastUpdated);

                                int result = await cmd.ExecuteNonQueryAsync();
                                if (result > 0)
                                {
                                    _playerRegistry.TryRemove(exists, out _);
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
                    Command = new OnboardPlayerDataCommand
                    {
                        PlayerId = playerId,
                        GamerTag = playerName,
                        Topic = CommandRegistry.OnboardPlayerData,
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
        private async Task<int> ProcessPlayerData(PlayerGraphQLResult queryData)
        {
            var players = new List<PlayerData>();
            if (queryData.TournamentLink == null) throw new ApplicationException("Player Query Data was null from Start.gg");
            foreach (var node in queryData.TournamentLink.Entrants.Nodes)
            {
                var firstRecord = node.Participants.FirstOrDefault();
                if (firstRecord == null) continue;
                if (!_playersCache.TryGetValue(firstRecord.Player.Id, out int databaseId))
                {
                    if (firstRecord.User == null) continue;
                    databaseId = await CheckDuplicatePlayer(firstRecord);
                    if (databaseId == 0)
                    {
                        var newPlayerData = new PlayerData
                        {
                            Id = await GenerateNewPlayerId(),
                            PlayerName = firstRecord.Player.GamerTag,
                            PlayerLinkID = firstRecord.Player.Id,
                            LastUpdate = DateTime.UtcNow,
                            UserLink = firstRecord.User.Id
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
        private async Task<int> CheckDuplicatePlayer(CommonParticipant participantRecord)
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
