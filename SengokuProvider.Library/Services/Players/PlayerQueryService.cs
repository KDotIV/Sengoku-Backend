using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Models.User;
using SengokuProvider.Library.Services.Common.Interfaces;

namespace SengokuProvider.Library.Services.Players
{
    public class PlayerQueryService : IPlayerQueryService
    {
        private readonly string _connectionString;
        private readonly ICommonDatabaseService _commonDatabaseServices;
        private readonly IConfiguration _configuration;

        public PlayerQueryService(string connectionString, IConfiguration config, ICommonDatabaseService commonServices)
        {
            _connectionString = connectionString;
            _configuration = config;
            _commonDatabaseServices = commonServices;
        }
        public async Task<List<PlayerData>> GetRegisteredPlayersByTournamentId(int[] tournamentIds)
        {
            if (tournamentIds.Length == 0 || tournamentIds.Length < 0) { List<PlayerData> badResult = new List<PlayerData>(); Console.WriteLine("TournamentId cannot be invalid"); return badResult; }

            var result = await QueryPlayerDataByTournamentId(tournamentIds);

            return result;
        }
        public async Task<List<PlayerStandingResult>> GetStandingsDataByPlayerIds(int[] playerIds, int[] tournamentIds)
        {
            return await QueryStandingsByPlayerIds(playerIds, tournamentIds);
        }
        public async Task<List<PlayerStandingResult>> GetPlayerStandingResults(GetPlayerStandingsCommand command)
        {
            List<PlayerStandingResult> playerStandingResults = new List<PlayerStandingResult>();
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT standings.entrant_id, standings.player_id, standings.tournament_link, 
                                                        standings.placement, standings.entrants_num, standings.active, standings.last_updated,
                                                        players.player_name, tournament_links.event_link, tournament_links.id, tournament_links.url_slug, 
                                                        events.event_name
                                                        FROM standings
                                                        JOIN players ON standings.player_id = players.id
                                                        JOIN tournament_links ON standings.tournament_link = tournament_links.id
                                                        JOIN events ON events.link_id = tournament_links.id
                                                        WHERE standings.player_id = @Input
                                                        ORDER BY standings.active DESC;", conn))
                    {
                        cmd.Parameters.AddWithValue("@Input", command.PlayerId);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows)
                            {
                                Console.WriteLine("No standings found for the provided player ID.");
                                return playerStandingResults;
                            }
                            while (await reader.ReadAsync())
                            {
                                playerStandingResults.Add(ParseStandingsRecords(reader));
                            }
                        }
                    }
                }
                return playerStandingResults;
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
        public async Task<List<PlayerStandingResult>> GetPlayerStandingsCodexModule(int[] playerIds, int[] tournamentIds, int[] gameIds, DateTime startDate = default, DateTime endDate = default)
        {
            var playerResults = new List<PlayerStandingResult>();

            if (playerIds.Length < 1) return playerResults;
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT standings.entrant_id, standings.player_id, standings.tournament_link, 
                                                        standings.placement, standings.entrants_num,
                                                        players.player_name, tournament_links.event_link,
                                                        tournament_links.url_slug, events.event_name, events.end_time, standings.last_updated
                                                        FROM standings
                                                        JOIN players ON standings.player_id = players.id
                                                        JOIN tournament_links ON standings.tournament_link = tournament_links.id
                                                        JOIN events ON events.link_id = tournament_links.event_link
                                                        WHERE standings.player_id = @PlayerArray 
                                                        and tournament_links.game_id = @TournamentsArray
                                                        and start_time = @StartTimeInput
                                                        and end_time = @EndTimeInput
                                                        and tournament_links.game_id = @GamesArray
                                                        ORDER BY players.player_name, events.end_time ASC;", conn))
                    {
                        cmd.Parameters.Add(_commonDatabaseServices.CreateDBIntArrayType("@TournamentsArray", tournamentIds));
                        cmd.Parameters.Add(_commonDatabaseServices.CreateDBIntArrayType("@PlayerArray", playerIds));
                        cmd.Parameters.AddWithValue("@StartTimeInput", startDate);
                        cmd.Parameters.AddWithValue("@EndTimeInput", endDate);
                        cmd.Parameters.AddWithValue("@GamesArray", gameIds);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows)
                            {
                                Console.WriteLine("No standings were found for given Ids. Check parameters...");
                                return playerResults;
                            }
                            while (await reader.ReadAsync())
                            {
                                playerResults.Add(ParseStandingsRecords(reader));
                            }
                        }
                    }
                    ;
                }
                return playerResults;
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
        public async Task<List<PlayerTournamentCard>> GetTournamentCardsByPlayerIDs(int[] playerIds)
        {
            if (playerIds.Length < 1) throw new ArgumentException("playerIds cannot be empty or invalid array");

            var playerCards = new List<PlayerTournamentCard>();
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    const string sql = @"SELECT * FROM get_player_standings(@PlayerIds)";

                    var flatRows = await conn.QueryAsync<FlatPlayerStandings>(sql, new { PlayerIds = playerIds });

                    playerCards = flatRows.GroupBy(r => r.PlayerID)
                        .Select(g =>
                        {
                            var firstRecord = g.First();
                            return new PlayerTournamentCard
                            {
                                PlayerID = g.Key,
                                PlayerName = firstRecord.PlayerName,
                                PlayerResults = g.Take(10) //takes the top 10 from player
                                .Select(r => new PlayerStandingResult
                                {
                                    StandingDetails = new StandingDetails
                                    {
                                        GamerTag = firstRecord.PlayerName,
                                        TournamentId = r.Tournament_Link,
                                        Placement = r.Placement
                                    },
                                    LastUpdated = r.LastUpdated,
                                    EntrantsNum = r.EntrantsNum,
                                }).ToList()
                            };
                        }).ToList();
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

            return playerCards;
        }
        public async Task<UserPlayerData> GetUserDataByUserLink(int userLink)
        {
            return await QueryUserDataByUserLink(userLink);
        }
        public Task<UserPlayerData> GetUserDataByPlayerName(string playerName)
        {
            throw new NotImplementedException();
        }
        public async Task<PlayerData> GetPlayerByName(string playerName)
        {
            var result = new PlayerData
            {
                Id = 0,
                PlayerName = "",
                PlayerLinkID = 0,
                UserLink = 0,
                LastUpdate = DateTime.UtcNow
            };
            return await QueryPlayersByPlayerName(playerName, result);
        }
        public async Task<List<Links>> GetPlayersByEntrantLinks(int[] entrantId)
        {
            return await QueryPlayersByEntrantLinks(entrantId);
        }
        private async Task<List<Links>> QueryPlayersByEntrantLinks(int[] entrantIds)
        {
            if (entrantIds.Length < 1)
            {
                Console.WriteLine("Entrant IDs cannot be empty or invalid array");
                return new List<Links>();
            }
            var result = new List<Links>();
            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                using (var cmd = new NpgsqlCommand(@"SELECT player_id, entrant_id FROM standings WHERE entrant_id = ANY(@entrantIds)", conn))
                {
                    cmd.Parameters.AddWithValue("entrantIds", entrantIds);
                    using (var reader = await cmd.ExecuteReaderAsync())
                    {
                        if (!reader.HasRows)
                        {
                            Console.WriteLine("No player found for the provided entrant ID.");
                            return result;
                        }
                        while (await reader.ReadAsync())
                        {
                            result.Add(new Links
                            {
                                PlayerId = reader.GetInt32(reader.GetOrdinal("player_id")),
                                EntrantId = reader.GetInt32(reader.GetOrdinal("entrant_id"))
                            });
                        }
                    }
                }
                return result;
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.InnerException}", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        private async Task<List<PlayerStandingResult>> QueryStandingsByPlayerIds(int[] playerIds, int[] tournamentIds)
        {
            if (playerIds.Length < 1 || tournamentIds.Length < 1)
            {
                Console.WriteLine("Player or Tournament Ids cannot be empty or invalid array");
                return new List<PlayerStandingResult>();
            }
            var playerResults = new List<PlayerStandingResult>();

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT s.entrant_id, s.player_id, s.tournament_link, s.placement, s.entrants_num, s.active, tl.url_slug, tl.event_link, s.last_updated
                                                            FROM standings s JOIN tournament_links tl ON s.tournament_link = tl.id 
                                                            WHERE player_id = ANY(@playerIds) 
                                                            AND tournament_link = ANY(@tournamentIds);", conn))
                    {
                        cmd.Parameters.AddWithValue("playerIds", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer, playerIds);
                        cmd.Parameters.AddWithValue("tournamentIds", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Integer, tournamentIds);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows)
                            {
                                Console.WriteLine("No standings found for the provided player IDs and tournament IDs.");
                                return playerResults;
                            }
                            while (await reader.ReadAsync())
                            {
                                playerResults.Add(new PlayerStandingResult
                                {
                                    EntrantsNum = reader.GetInt32(reader.GetOrdinal("entrants_num")),

                                    StandingDetails = new StandingDetails
                                    {
                                        IsActive = reader.GetBoolean(reader.GetOrdinal("active")),
                                        Placement = reader.GetInt32(reader.GetOrdinal("placement")),
                                        TournamentId = reader.GetInt32(reader.GetOrdinal("tournament_link")),
                                        TournamentName = _commonDatabaseServices.CleanUrlSlugName(reader.GetString(reader.GetOrdinal("url_slug"))),
                                        EventId = reader.GetInt32(reader.GetOrdinal("event_link"))
                                    },
                                    TournamentLinks = new Links
                                    {
                                        EntrantId = reader.GetInt32(reader.GetOrdinal("entrant_id")),
                                        PlayerId = reader.GetInt32(reader.GetOrdinal("player_id"))
                                    },
                                    LastUpdated = reader.GetDateTime(reader.GetOrdinal("last_updated"))
                                });
                            }
                            return playerResults;
                        }
                    }
                }
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.InnerException}", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        private async Task<PlayerData> QueryPlayersByPlayerName(string playerName, PlayerData result)
        {
            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                const string sql = @"SELECT * FROM public.players where player_name like '%@PlayerName%';";
                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@PlayerName", playerName);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        if (!reader.HasRows) return result;

                        result.Id = reader.GetInt32(reader.GetOrdinal("id"));
                        result.PlayerName = reader.GetString(reader.GetOrdinal("player_name"));
                        result.PlayerLinkID = reader.GetInt32(reader.GetOrdinal("startgg_link"));
                        result.UserLink = reader.GetInt32(reader.GetOrdinal("user_link"));
                        result.LastUpdate = reader.GetDateTime(reader.GetOrdinal("last_updated"));
                    }
                    return result;
                }
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.InnerException}", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        private async Task<UserPlayerData> MapUserPlayerDataAsync(UserPlayerData result, UserGraphQLResult queryResult)
        {
            if (queryResult.UserNode.Player == null) return result;

            int existing = await CheckExistingPlayerbase(queryResult.UserNode.Player.Id);

            if (existing > 0) { result.PlayerId = existing; }
            else { result.PlayerId = queryResult.UserNode.Player.Id; }
            result.PlayerName = queryResult.UserNode.Player.GamerTag ?? "";
            result.userLink = queryResult.UserNode.Id;

            return result;
        }
        public async Task<int> GetPlayerIdByStartGgId(int playerLinkId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            try
            {
                await conn.OpenAsync();

                using var cmd = new NpgsqlCommand(@"SELECT * FROM players where startgg_link = @Input", conn);
                cmd.Parameters.AddWithValue("@Input", playerLinkId);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        if (!reader.HasRows) return 0;

                        return reader.GetInt32(reader.GetOrdinal("id"));
                    }
                }
                return 0;
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.InnerException}", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }

        }
        private async Task<int> CheckExistingPlayerbase(int playerLinkId)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            try
            {
                await conn.OpenAsync();

                using var cmd = new NpgsqlCommand(@"SELECT * FROM players where startgg_link = @Input", conn);
                cmd.Parameters.AddWithValue("@Input", playerLinkId);
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        if (!reader.HasRows) return 0;

                        return reader.GetInt32(reader.GetOrdinal("id"));
                    }
                }
                return 0;
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.InnerException}", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }

        }
        private async Task<UserPlayerData> QueryUserDataByUserLink(int userLink)
        {
            var result = new UserPlayerData
            {
                PlayerId = 0,
                PlayerEmail = "",
                PlayerName = "",
                userLink = 0,
                GameIds = []
            };
            try
            {
                using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();

                const string sql = @"SELECT users.user_name, users.email, users.user_link, p.id, p.player_name FROM users 
                                        JOIN players p ON users.player_id = p.id 
                                        WHERE users.user_link = @UserLink;";

                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@UserLink", userLink);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        if (!reader.HasRows) return result;

                        result.PlayerId = reader.GetInt32(reader.GetOrdinal("id"));
                        result.PlayerEmail = reader.GetString(reader.GetOrdinal("email"));
                        result.PlayerName = reader.GetString(reader.GetOrdinal("player_name"));
                        result.userLink = reader.GetInt32(reader.GetOrdinal("user_link"));
                    }
                    return result;
                }
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.InnerException}", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Unexpected Error Occurred: {ex.StackTrace}", ex);
            }
        }
        private PlayerStandingResult ParseStandingsRecords(NpgsqlDataReader reader)
        {
            return new PlayerStandingResult
            {
                StandingDetails = new StandingDetails
                {
                    IsActive = reader.IsDBNull(reader.GetOrdinal("active")) ? false : reader.GetBoolean(reader.GetOrdinal("active")),
                    Placement = reader.IsDBNull(reader.GetOrdinal("placement")) ? 0 : reader.GetInt32(reader.GetOrdinal("placement")),
                    GamerTag = reader.IsDBNull(reader.GetOrdinal("player_name")) ? "" : reader.GetString(reader.GetOrdinal("player_name")),
                    EventId = reader.IsDBNull(reader.GetOrdinal("event_link")) ? 0 : reader.GetInt32(reader.GetOrdinal("event_link")),
                    EventName = reader.IsDBNull(reader.GetOrdinal("event_name")) ? "" : reader.GetString(reader.GetOrdinal("event_name")),
                    TournamentId = reader.IsDBNull(reader.GetOrdinal("id")) ? 0 : reader.GetInt32(reader.GetOrdinal("id")),
                    TournamentName = reader.IsDBNull(reader.GetOrdinal("url_slug")) ? "" : reader.GetString(reader.GetOrdinal("url_slug"))
                },
                TournamentLinks = new Links
                {
                    EntrantId = reader.GetInt32(reader.GetOrdinal("entrant_id")),
                    PlayerId = reader.GetInt32(reader.GetOrdinal("player_id"))
                },
                EntrantsNum = reader.IsDBNull(reader.GetOrdinal("entrants_num")) ? 0 : reader.GetInt32(reader.GetOrdinal("entrants_num")),
                LastUpdated = reader.IsDBNull(reader.GetOrdinal("last_updated")) ? DateTime.MinValue : reader.GetDateTime(reader.GetOrdinal("last_updated")),
            };
        }
        private async Task<List<PlayerData>> QueryPlayerDataByTournamentId(int[] tournamentLinks)
        {
            List<PlayerData> playerResult = new List<PlayerData>();
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT * FROM get_players_by_tournament_link(@TournamentLinks)", conn))
                    {
                        cmd.Parameters.Add(_commonDatabaseServices.CreateDBIntArrayType("@TournamentLinks", tournamentLinks));

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows)
                            {
                                Console.WriteLine("No players found with that TournamentLink Id");
                                return playerResult;
                            }
                            while (await reader.ReadAsync())
                            {
                                playerResult.Add(new PlayerData
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                                    PlayerName = reader.GetString(reader.GetOrdinal("player_name")),
                                    UserLink = reader.GetInt32(reader.GetOrdinal("user_link")),
                                    PlayerLinkID = reader.GetInt32(reader.GetOrdinal("startgg_link")),
                                    LastUpdate = reader.GetDateTime(reader.GetOrdinal("last_updated"))
                                });
                            }
                        }
                    }
                }
                return playerResult;
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
