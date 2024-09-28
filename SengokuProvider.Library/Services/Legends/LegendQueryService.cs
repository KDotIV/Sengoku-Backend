using GraphQL.Client.Http;
using Npgsql;
using SengokuProvider.Library.Models.Leagues;
using SengokuProvider.Library.Models.Legends;
using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Services.Legends
{
    public class LegendQueryService : ILegendQueryService
    {
        private readonly string _connectString;
        private readonly GraphQLHttpClient _client;

        public LegendQueryService(string connectionString, GraphQLHttpClient graphQlClient)
        {
            _connectString = connectionString;
            _client = graphQlClient;
        }
        public async Task<List<LeagueByOrgResults>> GetLeaderboardsByOrgId(int OrgId)
        {
            if (OrgId < 0) return new List<LeagueByOrgResults>();
            try
            {
                using (var conn = new NpgsqlConnection(_connectString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"", conn))
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
                                    Id = reader.GetInt32(reader.GetOrdinal("id")),
                                    Name = reader.GetString(reader.GetOrdinal("name")),
                                    OrgId = reader.GetInt32(reader.GetOrdinal("org_id")),
                                    StartDate = reader.GetDateTime(reader.GetOrdinal("start_date")),
                                    EndDate = reader.GetDateTime(reader.GetOrdinal("end_date")),
                                    Game = reader.GetInt32(reader.GetOrdinal("game")),
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
        public async Task<List<LeaderboardData>> GetLeaderboardResultsByLeagueId(int leagueId)
        {
            if (leagueId < 1) return new List<LeaderboardData>();

            try
            {
                using (var conn = new NpgsqlConnection(_connectString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"SELECT p.player_name, SUM(s.gained_points) AS total_points,COUNT(DISTINCT s.tournament_link) AS tournament_count
                                                        FROM players p
                                                        JOIN player_leagues pl ON p.id = pl.player_id
                                                        JOIN standings s ON p.id = s.player_id
                                                        JOIN tournament_links t ON s.tournament_link = t.id
                                                        JOIN tournament_leagues tl ON t.id = tl.tournament_id
                                                        JOIN leagues l ON tl.league_id = l.id
                                                        WHERE l.id = @Input AND pl.league_id = @Input
                                                        GROUP BY p.player_name, l.name
                                                        ORDER BY total_points DESC;", conn))
                    {
                        cmd.Parameters.AddWithValue("@Input", leagueId);
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows) new List<LeaderboardData>();
                            var queryResult = new List<LeaderboardData>();

                            while (await reader.ReadAsync())
                            {
                                var mappedData = new LeaderboardData
                                {
                                    PlayerName = reader.GetString(reader.GetOrdinal("player_name")),
                                    TotalPoints = reader.GetInt32(reader.GetOrdinal("total_points")),
                                    TournamentCount = reader.GetInt32(reader.GetOrdinal("tournament_count"))
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
        public Task<LegendData> GetLegendByPlayerIds(List<int> playerID)
        {
            throw new NotImplementedException();
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
    }
}
