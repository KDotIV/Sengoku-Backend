using Dapper;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Npgsql;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Legends;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Services.Players;
using SengokuProvider.Library.Services.Users;
using SengokuProvider.Worker.Handlers;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SengokuProvider.Library.Services.Legends
{
    public class LegendIntakeService : ILegendIntakeService
    {
        private readonly ILegendQueryService _legendQueryService;
        private readonly string _connectionString;
        private static Random _rand = new Random();
        public LegendIntakeService(string connectionString, ILegendQueryService queryService)
        {
            _connectionString = connectionString;
            _legendQueryService = queryService;
        }
        public async Task<LegendData> BuildLegendData(StandingsQueryResult standings, string playerName)
        {
            ArgumentNullException.ThrowIfNull(standings);
            var existingLegendId = await CheckDuplicateLegend(standings.PlayerID);

            return new LegendData
            {
                Id = existingLegendId > 0 ? existingLegendId : await GenerateNewLegendId(),
                LegendName = "Placeholder Style",
                PlayerId = standings.PlayerID,
                PlayerLinkId = 0,
                PlayerName = playerName,
                Standings = standings.StandingData?.Select(standing => standing.Placement).ToList() ?? []
            };
        }
        public async Task<TournamentOnboardResult> AddTournamentToLeague(int[] tournamentIds, int leagueId)
        {
            var newOnboardResult = new TournamentOnboardResult { Response = "Open" };

            if (leagueId < 0) { newOnboardResult.Response = "LeagueId cannot be invalid ids"; }

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var transaction = await conn.BeginTransactionAsync())
                {
                    foreach (var tournamentId in tournamentIds)
                    {
                        try
                        {
                            using (var cmd = new NpgsqlCommand(@"INSERT INTO tournament_leagues (tournament_id, league_id, last_updated) VALUES (@TournamentInput, @LeagueInput, @LastUpdated) ON CONFLICT DO NOTHING;", conn))
                            {
                                cmd.Parameters.AddWithValue("@TournamentInput", tournamentId);
                                cmd.Parameters.AddWithValue("@LeagueInput", leagueId);
                                cmd.Parameters.AddWithValue("@LastUpdated", DateTime.UtcNow);

                                var result = await cmd.ExecuteNonQueryAsync();
                                if (result == 0)
                                {
                                    newOnboardResult.Response = "Tournament has already been added to League";
                                }
                                if (result > 0)
                                {
                                    newOnboardResult.Response = "Successfully Inserted Tournament to League";
                                    newOnboardResult.Successful.Add(tournamentId);
                                }
                            }
                        }
                        catch (NpgsqlException ex)
                        {
                            newOnboardResult.Response = ex.Message;
                            newOnboardResult.Failures.Add(tournamentId);
                            continue;
                        }
                        catch (Exception ex)
                        {
                            newOnboardResult.Response = ex.Message;
                            newOnboardResult.Failures.Add(tournamentId);
                            continue;
                        }
                    }
                    await transaction.CommitAsync();
                }
                await conn.CloseAsync();
            }
            return newOnboardResult;
        }
        public async Task<LeagueOnboardResult> AddPlayerToLeague(int[] playerIds, int leagueId)
        {
            if (playerIds.Length == 0) return new LeagueOnboardResult { Response = "PlayerId List cannot be Empty" };
            List<PlayerData> currentPlayers = await GetPlayersByIds(playerIds);

            var newOnboardResult = new LeagueOnboardResult { Response = "Open" };

            if (leagueId < 0) { newOnboardResult.Response = "PlayerId or LeagueId cannot be invalid ids"; }

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var transaction = await conn.BeginTransactionAsync())
                {
                    try
                    {
                        var insertQuery = new StringBuilder(@"INSERT INTO player_leagues (player_id, league_id, player_name, last_updated) VALUES ");
                        var parameters = new List<NpgsqlParameter>();
                        var valueCounter = 0;

                        foreach (var player in currentPlayers)
                        {
                            if (valueCounter > 0)
                            {
                                insertQuery.Append(", ");
                            }

                            insertQuery.Append($"(@PlayerInput{valueCounter}, @LeagueInput{valueCounter}, @PlayerName{valueCounter}, @LastUpdated{valueCounter})");

                            parameters.Add(new NpgsqlParameter($"@PlayerInput{valueCounter}", player.Id));
                            parameters.Add(new NpgsqlParameter($"@LeagueInput{valueCounter}", leagueId));
                            parameters.Add(new NpgsqlParameter($"@PlayerName{valueCounter}", player.PlayerName));
                            parameters.Add(new NpgsqlParameter($"@LastUpdated{valueCounter}", DateTime.UtcNow));

                            valueCounter++;
                        }

                        insertQuery.Append(" ON CONFLICT DO NOTHING;");

                        using (var cmd = new NpgsqlCommand(insertQuery.ToString(), conn))
                        {
                            cmd.Parameters.AddRange(parameters.ToArray());
                            var result = await cmd.ExecuteNonQueryAsync();

                            if (result == 0) { newOnboardResult.Response = "All Players have already been added to League"; }
                            if (result > 0)
                            {
                                newOnboardResult.Response = "Successfully Inserted Players to League";
                                newOnboardResult.Successful.AddRange(currentPlayers.Select(p => p.Id));
                            }
                        }

                        await transaction.CommitAsync();
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
            }
            return newOnboardResult;
        }
        public async Task<LeagueOnboardResult> AddPlayerToLeague(HashSet<int> playerIds, int leagueId)
        {
            return await AddPlayerToLeague(playerIds.ToArray(), leagueId);
        }
        public async Task<List<TournamentBoardResult>> AddTournamentsToRunnerBoard(int userId, int orgId, List<int> tournamentIds)
        {
            var success = await UpdateTournamentsToRunnerBoard(userId, tournamentIds, orgId);

            return await _legendQueryService.GetCurrentRunnerBoard(userId, orgId);
        }
        public async Task<UpdateLeaderboardResponse> UpdateLeaderboardStandingsByLeagueId(int[] leagueIds)
        {
            if (leagueIds.Length < 0) throw new ArgumentException($"Cannot Update an invalid LeagueId {nameof(leagueIds)}.");
            var previousResults = await _legendQueryService.GetCurrentLeaderBoardResults(leagueIds, []);
            var newResults = await _legendQueryService.GetLeaderboardResultsByLeagueId(leagueIds, 2);
            return await UpdateCurrentLeaderboardResults(previousResults, newResults);
        }
        public async Task<int> InsertNewLegendData(LegendData newLegend)
        {
            if (newLegend == null) { throw new ArgumentNullException(nameof(newLegend)); }
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    await using var transaction = await conn.BeginTransactionAsync();
                    await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@key, 2))",
                        new { key = $"legend:{newLegend.PlayerId}" }, transaction);
                    var existingId = await conn.QueryFirstOrDefaultAsync<int>("SELECT id FROM legends WHERE player_id = @PlayerId ORDER BY id LIMIT 1",
                        new { newLegend.PlayerId }, transaction);
                    if (existingId > 0)
                    {
                        if (newLegend.PlayerLinkId > 0)
                            await conn.ExecuteAsync("UPDATE legends SET player_link_id = @PlayerLinkId WHERE id = @existingId",
                                new { newLegend.PlayerLinkId, existingId }, transaction);
                        await transaction.CommitAsync();
                        return existingId;
                    }
                    await conn.ExecuteAsync("SELECT pg_advisory_xact_lock(728349103)", transaction: transaction);
                    while (await conn.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM legends WHERE id = @Id)", new { newLegend.Id }, transaction))
                        newLegend.Id = _rand.Next(100000, 1000000);
                    using (var cmd = new NpgsqlCommand(@"INSERT INTO legends (id, legend_name, player_name, player_id, player_link_id, standings, last_updated) 
                    VALUES (@Id, @LegendName, @PlayerName, @PlayerId, @PlayerLink, @Standings, @LastUpdated)
                    RETURNING id", conn, transaction))
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
                        await transaction.CommitAsync();
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
        public async Task<int> InsertNewLegendData(List<LegendData> legendData)
        {
            if (legendData == null || legendData.Count == 0) { throw new ArgumentNullException(nameof(legendData)); }
            int insertedCount = 0;
            foreach (var legend in legendData)
            {
                var result = await InsertNewLegendData(legend);
                if (result > 0) insertedCount++;
            }
            return insertedCount;
        }
        public async Task<LeagueByOrgResults> InsertNewLeagueByOrg(int orgId, string leagueName, DateTime startDate, DateTime endDate, int gameId = 0, string description = "")
        {
            LeagueByOrgResults result = new LeagueByOrgResults { LeagueId = 0, LeagueName = "default", OrgId = orgId, StartDate = startDate, EndDate = endDate, LastUpdate = DateTime.UtcNow, IsActive = false };

            if (orgId < 0) return result;
            bool hasLeague = await CheckExistingLeagues(orgId, leagueName);

            if (hasLeague) return result;
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"INSERT INTO leagues (id, name, org_id, start_date, end_date, description, game, last_updated) 
                                                        VALUES (@LeagueId, @LeagueName, @OrgId, @StartDate, @EndDate, @Description, @Game, @LastUpdated) 
                                                        ON CONFLICT (id) DO UPDATE SET
                                                            name = EXCLUDED.name,
                                                            org_id = EXCLUDED.org_id,
                                                            start_date = EXCLUDED.start_date,
                                                            end_date = EXCLUDED.end_date,
                                                            description = EXCLUDED.description,
                                                            game = EXCLUDED.game,
                                                            last_updated = EXCLUDED.last_updated;", conn))
                    {
                        var tempId = await GenerateNewLeagueId();
                        cmd.Parameters.AddWithValue(@"LeagueId", tempId);
                        cmd.Parameters.AddWithValue(@"Leaguename", leagueName);
                        cmd.Parameters.AddWithValue(@"OrgId", orgId);
                        cmd.Parameters.AddWithValue(@"StartDate", startDate);
                        cmd.Parameters.AddWithValue(@"EndDate", endDate);
                        cmd.Parameters.AddWithValue(@"Description", description);
                        cmd.Parameters.AddWithValue(@"Game", gameId);
                        cmd.Parameters.AddWithValue(@"LastUpdated", DateTime.UtcNow);

                        var insertResult = await cmd.ExecuteNonQueryAsync();
                        if (insertResult > 0)
                        {
                            result.LeagueId = tempId;
                            result.LeagueName = leagueName;
                            result.OrgId = orgId;
                            result.StartDate = startDate;
                            result.EndDate = endDate;
                            result.Game = gameId;
                            result.LastUpdate = DateTime.UtcNow;
                            result.Response = "Successful";
                        }
                    }
                }
                return result;
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
        private async Task<UpdateLeaderboardResponse> UpdateCurrentLeaderboardResults(List<LeaderboardData> previousResults, List<LeaderboardData> newResults)
        {
            if (newResults.Count == 0) throw new ArgumentException($"New Results cannot be empty {nameof(newResults)}.");

            // Compare previous and new leaderboard results by the frequency of playerId
            var tempDict = previousResults.ToDictionary(k => k.PlayerId, v => v.CurrentScore);

            // Ensures the state of the updated results
            var updatedResults = new List<LeaderboardData>();

            foreach (var updatedResult in newResults)
            {
                var resultCopy = new LeaderboardData
                {
                    PlayerId = updatedResult.PlayerId,
                    PlayerName = updatedResult.PlayerName,
                    LeagueId = updatedResult.LeagueId,
                    GainedPoints = updatedResult.GainedPoints,
                    TournamentId = updatedResult.TournamentId,
                    UrlSlug = updatedResult.UrlSlug,
                    TournamentCount = updatedResult.TournamentCount,
                    ScoreChange = 0, // Default, will be updated
                    LastUpdated = DateTime.UtcNow
                };

                if (tempDict.TryGetValue(updatedResult.PlayerId, out int currentValue))
                {
                    resultCopy.ScoreChange = updatedResult.CurrentScore - currentValue;
                }

                updatedResults.Add(resultCopy);
            }

            return await IntakeUpdatedLeaderboardresults(updatedResults);
        }
        private async Task<UpdateLeaderboardResponse> IntakeUpdatedLeaderboardresults(List<LeaderboardData> updatedResults)
        {
            var updatedResponse = new UpdateLeaderboardResponse { SuccessfulPayers = new List<int>(), FailedPlayers = new List<int>(), Message = "" };
            if (updatedResults.Count == 0) { updatedResponse.Message = "No new Leaderboard Results to Update"; return updatedResponse; }

            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                using (var transaction = await conn.BeginTransactionAsync())
                {
                    foreach (var updatedRecord in updatedResults)
                    {
                        try
                        {
                            using (var cmd = new NpgsqlCommand(@"UPDATE player_leagues AS pl
                                                            SET 
                                                                last_updated = upval.last_updated,
                                                                current_score = upval.current_score,
                                                                score_change = upval.score_change,
                                                                player_name = upval.player_name
                                                            FROM (
                                                                VALUES (@PlayerId, @LeagueId, CURRENT_DATE, @CurrentScore, @ScoreChange, @PlayerName)
                                                            ) AS upval(player_id, league_id, last_updated, current_score, score_change, player_name)
                                                            WHERE pl.player_id = upval.player_id AND pl.league_id = upval.league_id;", conn))
                            {
                                cmd.Parameters.AddWithValue("@PlayerId", updatedRecord.PlayerId);
                                cmd.Parameters.AddWithValue("@LeagueId", updatedRecord.LeagueId);
                                cmd.Parameters.AddWithValue("@CurrentScore", updatedRecord.CurrentScore);
                                cmd.Parameters.AddWithValue("@ScoreChange", updatedRecord.ScoreChange);
                                cmd.Parameters.AddWithValue("@PlayerName", updatedRecord.PlayerName);
                                var result = await cmd.ExecuteNonQueryAsync();
                                if (result > 0) updatedResponse.SuccessfulPayers.Add(result);
                                else updatedResponse.FailedPlayers.Add(result);
                            }
                        }
                        catch (NpgsqlException ex)
                        {
                            Console.WriteLine(new ApplicationException("Database error occurred: ", ex));
                            updatedResponse.FailedPlayers.Add(updatedRecord.PlayerId);
                            continue;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(new ApplicationException("Unexpected Error Occurred: ", ex));
                            updatedResponse.FailedPlayers.Add(updatedRecord.PlayerId);
                            continue;
                        }
                    }
                    await transaction.CommitAsync();
                }
                await conn.CloseAsync();
            }
            return updatedResponse;
        }
        private async Task<bool> UpdateTournamentsToRunnerBoard(int userId, List<int> tournamentIds, int orgId = 0)
        {
            if (userId <= 0 || orgId < 0) return false;

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"WITH new_ids AS (
                                                            SELECT ARRAY[@NewTournaments]::int[] AS new_tournament_ids
                                                        ),
                                                        existing_tournament_ids AS (
                                                            SELECT unnest(tournament_links) AS tournament_id
                                                            FROM bracket_boards
                                                            WHERE user_id = @UserInput AND organization_id = @OrgInput
                                                        ),
                                                        combined_ids AS (
                                                            SELECT array_agg(DISTINCT tournament_id) || new_ids.new_tournament_ids AS updated_tournament_links
                                                            FROM existing_tournament_ids, new_ids
                                                        )

                                                        UPDATE bracket_boards
                                                        SET tournament_links = (SELECT updated_tournament_links FROM combined_ids)
                                                        WHERE user_id = @UserInput AND organization_id = @OrgInput;"))
                    {
                        cmd.Parameters.AddWithValue("@NewTournaments", tournamentIds.ToArray());
                        cmd.Parameters.AddWithValue("@UserInput", userId);
                        cmd.Parameters.AddWithValue("@OrgInput", orgId);

                        var updated = await cmd.ExecuteNonQueryAsync();
                        if (updated > 0)
                        {
                            return true;
                        }
                    }
                }
                return false;
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
        private async Task<List<PlayerData>> GetPlayersByIds(int[] playerIds)
        {
            if (playerIds.Length == 0) { throw new ArgumentException("PlayerIds cannot be empty"); }

            var playerList = new List<PlayerData>();

            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT * FROM players where id = ANY(@PlayerInput);", conn))
                    {
                        cmd.Parameters.AddWithValue("@PlayerInput", playerIds);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows) return playerList;
                            while (await reader.ReadAsync())
                            {
                                playerList.Add(new PlayerData
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                                    PlayerName = reader.GetString(reader.GetOrdinal("player_name")),
                                    Style = reader.IsDBNull(reader.GetOrdinal("style")) ? null : reader.GetString(reader.GetOrdinal("style")),
                                    PlayerLinkID = reader.GetInt32(reader.GetOrdinal("startgg_link")),
                                    UserId = reader.IsDBNull(reader.GetOrdinal("user_id")) ? default : reader.GetInt32(reader.GetOrdinal("user_id")),
                                    UserLink = reader.GetInt32(reader.GetOrdinal("user_link")),
                                    LastUpdate = reader.GetDateTime(reader.GetOrdinal("last_updated"))
                                });
                            }
                        }
                    }
                }
                return playerList;
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
        private async Task<bool> CheckExistingLeagues(int orgId, string leagueName)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT l.id, l.name, l.start_date, l.end_date, l.last_updated 
                                                        FROM leagues l 
                                                        JOIN organization_leagues ol ON l.id = ol.league_id 
                                                        WHERE ol.organization_id = @Input;", conn))
                    {
                        cmd.Parameters.AddWithValue("@Input", orgId);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows) return false;

                            int leagueCount = 0;
                            var leagueNames = new HashSet<string>();

                            while (await reader.ReadAsync())
                            {
                                leagueCount++;
                                leagueNames.Add(reader.GetString(reader.GetOrdinal("name")));

                                if (leagueCount > 4) return true;
                            }

                            // Check if the specified leagueName exists in the HashSet
                            return leagueNames.Contains(leagueName);
                        }
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
        private async Task<int> GenerateNewLeagueId()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                while (true)
                {
                    var newId = _rand.Next(100000, 1000000);

                    var newQuery = @"SELECT id FROM leagues where id = @Input";
                    var queryResult = await conn.QueryFirstOrDefaultAsync<int>(newQuery, new { Input = newId });
                    if (newId != queryResult || queryResult == 0) return newId;
                }
            }
        }
    }
}
