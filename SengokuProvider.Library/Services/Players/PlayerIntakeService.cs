using Dapper;
using Npgsql;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Legends;
using System.Collections.Concurrent;

namespace SengokuProvider.Library.Services.Players
{
    public class PlayerIntakeService : IPlayerIntakeService
    {
        private readonly IPlayerQueryService _queryService;
        private readonly ILegendQueryService _legendQueryService;

        private readonly string _connectionString;
        private ConcurrentDictionary<int, int> _playersCache;
        private ConcurrentDictionary<int, string> _playerRegistry;
        private HashSet<int> _eventCache;
        private int _currentEventId;
        private static Random _rand = new Random();
        public PlayerIntakeService(string connectionString, IPlayerQueryService playerQueryService, 
            ILegendQueryService legendQueryService)
        {
            _connectionString = connectionString;
            _queryService = playerQueryService;
            _legendQueryService = legendQueryService;
            _playersCache = new ConcurrentDictionary<int, int>();
            _playerRegistry = new ConcurrentDictionary<int, string>();
            _eventCache = new HashSet<int>();
        }
        public async Task<int> IntakePlayerData(IntakePlayersByTournamentCommand command)
        {
            try
            {
                PlayerGraphQLResult? newPlayerData = await _queryService.GetPlayerDataFromStartgg(command);
                if(newPlayerData == null) { return 0; }

                _eventCache.Add(newPlayerData.Data.Id);
                _currentEventId = newPlayerData.Data.Id;
                int playerSuccess =  await ProcessPlayerData(newPlayerData);

                var standingsSuccess = await ProcessLegendsFromNewPlayers(_playerRegistry);

                Console.WriteLine($"{standingsSuccess} total standings added for player");

                return playerSuccess;
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred during Player Intake: {ex.StackTrace}", ex);
            }
        }

        private async Task<int> ProcessLegendsFromNewPlayers(ConcurrentDictionary<int, string> registeredPlayers)
        {
            var currentStandings = new List<PlayerStandingResult>();
            if (registeredPlayers.Count == 0) throw new ApplicationException("Players must exist before new standings data is created");
            foreach (var newPlayer in registeredPlayers)
            {
                PlayerStandingResult newStanding = await _queryService.QueryPlayerStandings(new GetPlayerStandingsCommand { EventId = _currentEventId, GamerTag = newPlayer.Value, PerPage = 20 });
                if (newStanding == null) { continue; }

                newStanding.TournamentLinks.PlayerId = newPlayer.Key;
                currentStandings.Add(newStanding);
            }
            return await IntakePlayerStandingData(currentStandings);
        }

        private async Task<int> IntakePlayerStandingData(List<PlayerStandingResult> currentStandings)
        {
            var totalSuccess = 0;
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var transaction = await conn.BeginTransactionAsync())
                {
                    foreach (var data in currentStandings)
                    {
                        var createInsertCommand = @"
                            INSERT INTO standings (entrant_id, player_id, tournament_link, placement, entrants_num, active)
                            VALUES (@EntrantInput, @PlayerId, @TournamentLink, @PlacementInput, @NumEntrants, @IsActive)
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

                            int result = await cmd.ExecuteNonQueryAsync();
                            if(result > 0) 
                            { 
                                _playerRegistry.TryRemove(data.TournamentLinks.PlayerId, out _);
                                Console.WriteLine("Player removed from registry");
                            }
                        }
                    }
                    await transaction.CommitAsync();
                }
            }
            return totalSuccess;
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
                    if(databaseId == 0)
                    {
                        var newPlayerData = new PlayerData
                        {
                            Id = await GenerateNewPlayerId(),
                            PlayerName = firstRecord.Player.GamerTag,
                            PlayerLinkID = firstRecord.Player.Id,
                        };
                        players.Add(newPlayerData);
                    }
                }
            }
            return await InsertNewPlayerData(players);
        }
        private async Task<int> InsertNewPlayerData(List<PlayerData> players)
        {
            int totalSuccess = 0;
            using(var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using(var transaction = await conn.BeginTransactionAsync())
                {
                    foreach (var player in players)
                    {
                        var createInsertCommand = @"
                            INSERT INTO players (id, player_name, startgg_link)
                            VALUES (@IdInput, @PlayerName, @PlayerLinkId)
                            ON CONFLICT (id) DO UPDATE SET
                                player_name = EXCLUDED.player_name,
                                startgg_link = EXCLUDED.startgg_link;";
                        using(var cmd = new NpgsqlCommand(createInsertCommand, conn))
                        {
                            cmd.Transaction = transaction;
                            cmd.Parameters.AddWithValue("@IdInput",player.Id);
                            cmd.Parameters.AddWithValue("@PlayerName", player.PlayerName);
                            cmd.Parameters.AddWithValue("@PlayerLinkId", player.PlayerLinkID);

                            int result = await cmd.ExecuteNonQueryAsync();
                            if(result > 0) { _playerRegistry.TryAdd(player.Id, player.PlayerName); } totalSuccess += result;
                        }
                    }
                    await transaction.CommitAsync();
                }
            }
            return totalSuccess;
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
                    if (databaseId != 0) _playersCache.TryAdd(participantRecord.Player.Id , databaseId);

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
