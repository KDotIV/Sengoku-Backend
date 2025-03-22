using Dapper;
using GraphQL.Client.Http;
using Npgsql;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Legends;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Library.Services.Events;

namespace SengokuProvider.Library.Services.Legends
{
    public class LegendQueryService : ILegendQueryService
    {
        private readonly string _connectString;
        private readonly GraphQLHttpClient _client;
        private readonly ICommonDatabaseService _commonServices;
        private readonly IEventQueryService _eventQueryService;

        public LegendQueryService(string connectionString, GraphQLHttpClient graphQlClient, ICommonDatabaseService commonServices, IEventQueryService eventQuery)
        {
            _connectString = connectionString;
            _client = graphQlClient;
            _commonServices = commonServices;
            _eventQueryService = eventQuery;
        }
        public async Task<List<LeagueByOrgResults>> GetLeaderboardsByOrgId(int OrgId)
        {
            return await QueryLeaderboardsByOrgId(OrgId);
        }
        public async Task<List<LeagueByOrgResults>> GetLeagueByLeagueIds(int[] leagueIds)
        {
            return await QueryLeagueByLeagueIds(leagueIds);
        }
        public async Task<List<LeaderboardData>> GetLeaderboardResultsByLeagueId(int[] leagueIds, int topN)
        {
            return await QueryLeaderboardResultsByLeagueIds(leagueIds);
        }
        public async Task<List<LeaderboardData>> GetCurrentLeaderBoardResults(int[] leagueIds, int[] playerIds)
        {
            if (leagueIds.Length == 0) throw new ArgumentException($"Cannot search with an invalid LeagueId {nameof(leagueIds)}");

            try
            {
                List<LeaderboardData> result = new List<LeaderboardData>();
                using (var conn = new NpgsqlConnection(_connectString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new NpgsqlCommand(@"SELECT * FROM player_leagues WHERE league_id = ANY(@LeagueIds) ORDER BY current_score DESC;", conn))
                    {
                        cmd.Parameters.Add(_commonServices.CreateDBIntArrayType("@LeagueIds", leagueIds));
                        if (playerIds.Length > 0)
                        {
                            cmd.CommandText = @"SELECT * FROM player_leagues WHERE player_id = ANY(@PlayerIds) AND league_id = ANY(@LeagueIds) ORDER BY current_score DESC;";
                            cmd.Parameters.Add(_commonServices.CreateDBIntArrayType("@PlayerIds", playerIds));
                        }
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows) return result;
                            while (await reader.ReadAsync())
                            {
                                var newLeaderboardData = new LeaderboardData
                                {
                                    PlayerId = reader.GetInt32(reader.GetOrdinal("player_id")),
                                    PlayerName = reader.GetString(reader.GetOrdinal("player_name")),
                                    LeagueId = reader.GetInt32(reader.GetOrdinal("league_id")),
                                    CurrentScore = reader.GetInt32(reader.GetOrdinal("current_score")),
                                    ScoreChange = reader.GetInt32(reader.GetOrdinal("score_change")),
                                    LastUpdated = reader.GetDateTime(reader.GetOrdinal("last_updated"))
                                };
                                result.Add(newLeaderboardData);
                            }
                        }
                    }
                    ;
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
        public Task<LegendData> GetLegendByPlayerIds(List<int> playerID)
        {
            throw new NotImplementedException();
        }
        public async Task<List<LeagueRegionsResult>> GetLeagueRegions(int[] leagueIds)
        {
            return await QueryLeaguesByRegions(leagueIds);
        }
        public async Task<List<LeagueRegionsResult>> GetAllActiveLeagueRegions()
        {
            return await QueryActiveLeaguesByRegions();
        }
        public async Task<LegendData?> GetLegendsByPlayerLink(GetLegendsByPlayerLinkCommand command)
        {
            return await QueryLegendsByPlayerLink(command.PlayerLinkId);
        }
        public async Task<StandingsQueryResult?> QueryStandingsByPlayerId(int playerId)
        {
            if (playerId == 0) return null;

            try
            {
                using (var conn = new NpgsqlConnection(_connectString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new NpgsqlCommand(@"SELECT * FROM standings WHERE player_id = @Input AND last_updated BETWEEN CURRENT_DATE - INTERVAL '5 days' AND CURRENT_DATE;", conn))
                    {
                        cmd.Parameters.AddWithValue("@Input", playerId);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows) return null;

                            var queryResult = new StandingsQueryResult
                            {
                                PlayerID = playerId,
                                StandingData = new List<StandingsResult>()
                            };
                            while (await reader.ReadAsync())
                            {
                                var newStanding = new StandingsResult
                                {
                                    EntrantID = reader.GetInt32(reader.GetOrdinal("entrant_id")),
                                    TournamentLink = reader.GetInt32(reader.GetOrdinal("tournament_link")),
                                    EntrantsNum = reader.GetInt32(reader.GetOrdinal("entrants_num")),
                                    Placement = reader.GetInt32(reader.GetOrdinal("placement")),
                                    IsActive = reader.GetBoolean(reader.GetOrdinal("active"))
                                };
                                queryResult.StandingData.Add(newStanding);
                            }
                            return queryResult;
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
        public async Task<List<TournamentBoardResult>> GetCurrentRunnerBoard(int userId, int orgId = 0)
        {
            var tournamentBoardResult = new List<TournamentBoardResult>();
            if (userId <= 0 || orgId < 0) return tournamentBoardResult;

            try
            {
                using (var query = new NpgsqlCommand(@"WITH selectedTournamentIds AS (
	                                                    SELECT unnest(tournament_links) AS tournament_id
	                                                    FROM bracket_boards
	                                                    WHERE user_id = @UserInput AND organization_id = @OrgInput
                                                    )
                                                    SELECT 
	                                                    tl.id,
	                                                    tl.url_slug,
	                                                    tl.entrants_num,
	                                                    tl.last_updated,
	                                                    COALESCE(tl.game_id, 0)
                                                    FROM tournament_links tl
                                                    JOIN selectedTournamentIds sti ON tl.id = sti.tournament_id;",
                                                    new NpgsqlConnection(_connectString)))
                {
                    query.Parameters.AddWithValue("@UserInput", userId);
                    query.Parameters.AddWithValue("@OrgInput", orgId);
                    using (var reader = await query.ExecuteReaderAsync())
                    {
                        if (!reader.HasRows) return tournamentBoardResult;
                        while (await reader.ReadAsync())
                        {
                            var tournamentBoard = new TournamentBoardResult
                            {
                                TournamentId = reader.GetInt32(reader.GetOrdinal("id")),
                                TournamentName = _commonServices.CleanUrlSlugName(reader.GetString(reader.GetOrdinal("url_slug"))),
                                UrlSlug = reader.GetString(reader.GetOrdinal("url_slug")),
                                EntrantsNum = reader.GetInt32(reader.GetOrdinal("entrants_num")),
                                LastUpdated = reader.GetDateTime(reader.GetOrdinal("last_updated"))
                            };
                            tournamentBoardResult.Add(tournamentBoard);
                        }
                    }
                }
                return tournamentBoardResult;
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
        public async Task<List<LeagueTournamentData>> GetLeagueTournamentScheduleByLeagueId(int[] leagueIds)
        {
            if (leagueIds.Length < 0) throw new ArgumentException($"League Id must be valid");

            try
            {
                var result = new List<LeagueTournamentData>();
                using (var conn = new NpgsqlConnection(_connectString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT tlink.id, tleague.league_id, e.event_name, tlink.url_slug, tlink.viewership, tlink.player_ids, tlink.game_id, tlink.entrants_num, e.start_time, 
                                                        tlink.last_updated FROM tournament_links tlink
                                                        JOIN tournament_leagues tleague ON tlink.id = tleague.tournament_id
                                                        JOIN leagues l ON l.id = tleague.league_id
                                                        JOIN events e ON tlink.event_link = e.link_id
                                                        WHERE tleague.league_id = ANY(@LeagueIds);", conn))
                    {
                        cmd.Parameters.AddWithValue("@LeagueIds", leagueIds);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows) return result;
                            while (await reader.ReadAsync())
                            {
                                SqlMapper.AddTypeHandler(new GenericArrayHandler<int>());
                                var newTournamentData = new LeagueTournamentData
                                {
                                    LeagueId = reader.GetInt32(reader.GetOrdinal("league_id")),
                                    TournamentLinkId = reader.GetInt32(reader.GetOrdinal("id")),
                                    TournamentName = reader.GetString(reader.GetOrdinal("event_name")),
                                    UrlSlug = reader.GetString(reader.GetOrdinal("url_slug")),
                                    PlayerIds = reader.GetFieldValue<int[]>(reader.GetOrdinal("player_ids")) ?? Array.Empty<int>(),
                                    ViewershipUrls = reader.GetFieldValue<string[]>(reader.GetOrdinal("viewership")),
                                    EntrantsNum = reader.GetInt32(reader.GetOrdinal("entrants_num")),
                                    GameId = reader.GetInt32(reader.GetOrdinal("game_id")),
                                    StartTime = reader.GetDateTime(reader.GetOrdinal("start_time")),
                                    LastUpdated = reader.GetDateTime(reader.GetOrdinal("last_updated"))
                                };
                                result.Add(newTournamentData);
                            }
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
        public async Task<List<LeagueByOrgResults>> GetAvailableLeagues()
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT * FROM public.leagues WHERE end_date > CURRENT_DATE;", conn))
                    {
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows) new List<LeagueByOrgResults>();
                            var queryResult = new List<LeagueByOrgResults>();

                            while (await reader.ReadAsync())
                            {
                                var mappedData = new LeagueByOrgResults
                                {
                                    LeagueId = reader.GetInt32(reader.GetOrdinal("id")),
                                    LeagueName = reader.GetString(reader.GetOrdinal("name")),
                                    OrgId = reader.GetInt32(reader.GetOrdinal("org_id")),
                                    StartDate = reader.GetDateTime(reader.GetOrdinal("start_date")),
                                    EndDate = reader.GetDateTime(reader.GetOrdinal("end_date")),
                                    Game = reader.GetInt32(reader.GetOrdinal("game")),
                                    IsActive = reader.GetBoolean(reader.GetOrdinal("active")),
                                    LastUpdate = reader.GetDateTime(reader.GetOrdinal("last_updated"))
                                };
                                queryResult.Add(mappedData);
                            }
                            return queryResult;
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
        private async Task<LegendData?> QueryLegendsByPlayerLink(int playerLinkId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new NpgsqlCommand(@"SELECT * FROM legends WHERE player_link_id = @Input", conn))
                    {
                        cmd.Parameters.AddWithValue("@Input", playerLinkId);

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                return new LegendData
                                {
                                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                                    LegendName = reader.GetString(reader.GetOrdinal("legend_name")),
                                    PlayerName = reader.GetString(reader.GetOrdinal("player_name")),
                                    PlayerId = reader.GetInt32(reader.GetOrdinal("player_id")),
                                    PlayerLinkId = reader.GetInt32(reader.GetOrdinal("player_link_id"))
                                };
                            }
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
            return null;
        }
        private async Task<List<LeaderboardData>> QueryLeaderboardResultsByLeagueIds(int[] leagueIds)
        {
            if (leagueIds.Length < 1)
                return [];
            try
            {
                var result = new List<LeaderboardData>();

                using var conn = new NpgsqlConnection(_connectString);
                await conn.OpenAsync();

                const string sql = @"SELECT * FROM fn_top_scores_by_leagues(@LeagueIds, @TopN);";

                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@LeagueIds", leagueIds);
                cmd.Parameters.AddWithValue("@TopN", 2);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (!reader.HasRows) return result;
                    var data = new LeaderboardData
                    {
                        PlayerId = reader.GetInt32(reader.GetOrdinal("player_id")),
                        LeagueId = reader.GetInt32(reader.GetOrdinal("league_id")),
                        PlayerName = reader.GetString(reader.GetOrdinal("player_name")),
                        CurrentScore = reader.GetInt32(reader.GetOrdinal("current_score")),
                        LeagueName = reader.GetString(reader.GetOrdinal("league_name")),
                        ScoreChange = reader.GetInt32(reader.GetOrdinal("score_difference")),
                        TournamentCount = reader.GetInt32(reader.GetOrdinal("tournament_count")),
                        GameId = reader.GetInt32(reader.GetOrdinal("game_id")),
                        LastUpdated = reader.GetDateTime(reader.GetOrdinal("last_updated"))
                    };
                    result.Add(data);
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
        private async Task<List<LeagueByOrgResults>> QueryLeagueByLeagueIds(int[] leagueIds)
        {
            if (leagueIds.Length == 0) throw new ArgumentException($"Must contain valid LeagueId {nameof(leagueIds)}");
            try
            {
                using (var conn = new NpgsqlConnection(_connectString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT * FROM public.leagues WHERE id = ANY(@Input)", conn))
                    {
                        cmd.Parameters.AddWithValue("@Input", leagueIds);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows) new List<LeagueByOrgResults>();
                            var queryResult = new List<LeagueByOrgResults>();

                            while (await reader.ReadAsync())
                            {
                                var mappedData = new LeagueByOrgResults
                                {
                                    LeagueId = reader.GetInt32(reader.GetOrdinal("id")),
                                    LeagueName = reader.GetString(reader.GetOrdinal("name")),
                                    OrgId = reader.GetInt32(reader.GetOrdinal("org_id")),
                                    StartDate = reader.GetDateTime(reader.GetOrdinal("start_date")),
                                    EndDate = reader.GetDateTime(reader.GetOrdinal("end_date")),
                                    Game = reader.GetInt32(reader.GetOrdinal("game")),
                                    IsActive = reader.GetBoolean(reader.GetOrdinal("active")),
                                    LastUpdate = reader.GetDateTime(reader.GetOrdinal("last_updated"))
                                };
                                queryResult.Add(mappedData);
                            }
                            return queryResult;
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
        private async Task<List<LeagueByOrgResults>> QueryLeaderboardsByOrgId(int OrgId)
        {
            if (OrgId < 0) return new List<LeagueByOrgResults>();
            try
            {
                using (var conn = new NpgsqlConnection(_connectString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT * FROM leagues WHERE org_id = @Input", conn))
                    {
                        cmd.Parameters.AddWithValue("@Input", OrgId);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows) new List<LeagueByOrgResults>();
                            var queryResult = new List<LeagueByOrgResults>();

                            while (await reader.ReadAsync())
                            {
                                var mappedData = new LeagueByOrgResults
                                {
                                    LeagueId = reader.GetInt32(reader.GetOrdinal("id")),
                                    LeagueName = reader.GetString(reader.GetOrdinal("name")),
                                    OrgId = reader.GetInt32(reader.GetOrdinal("org_id")),
                                    StartDate = reader.GetDateTime(reader.GetOrdinal("start_date")),
                                    EndDate = reader.GetDateTime(reader.GetOrdinal("end_date")),
                                    Game = reader.GetInt32(reader.GetOrdinal("game")),
                                    IsActive = reader.GetBoolean(reader.GetOrdinal("active")),
                                    LastUpdate = reader.GetDateTime(reader.GetOrdinal("last_updated"))
                                };
                                queryResult.Add(mappedData);
                            }
                            return queryResult;
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
        private async Task<List<LeagueRegionsResult>> QueryLeaguesByRegions(int[] leagueIds)
        {
            if (leagueIds.Length == 0) throw new ArgumentException($"Cannot search with an invalid LeagueIds {nameof(leagueIds)}");

            try
            {
                using (var conn = new NpgsqlConnection(_connectString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT * FROM league_regions WHERE league_id = ANY(@Input) && active = true", conn))
                    {
                        cmd.Parameters.AddWithValue("@Input", leagueIds);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows) new List<LeagueRegionsResult>();
                            var queryResult = new List<LeagueRegionsResult>();
                            while (await reader.ReadAsync())
                            {
                                var mappedData = new LeagueRegionsResult
                                {
                                    LeagueId = reader.GetInt32(reader.GetOrdinal("league_id")),
                                    Regions = reader.GetFieldValue<string[]>(reader.GetOrdinal("regions")) ?? Array.Empty<string>(),
                                    GameId = reader.GetInt32(reader.GetOrdinal("game_id"))
                                };
                                queryResult.Add(mappedData);
                            }
                            return queryResult;
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
        private async Task<List<LeagueRegionsResult>> QueryActiveLeaguesByRegions()
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT * FROM league_regions WHERE active = true", conn))
                    {
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows) new List<LeagueRegionsResult>();
                            var queryResult = new List<LeagueRegionsResult>();
                            while (await reader.ReadAsync())
                            {
                                var mappedData = new LeagueRegionsResult
                                {
                                    GameId = reader.GetInt32(reader.GetOrdinal("game_id")),
                                    LeagueId = reader.GetInt32(reader.GetOrdinal("league_id")),
                                    Regions = reader.GetFieldValue<string[]>(reader.GetOrdinal("region_ids")) ?? Array.Empty<string>()
                                };
                                queryResult.Add(mappedData);
                            }
                            return queryResult;
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

    }
}
