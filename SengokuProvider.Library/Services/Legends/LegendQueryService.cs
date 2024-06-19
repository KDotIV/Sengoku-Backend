using GraphQL.Client.Http;
using Npgsql;
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

        public Task<LegendData> GetLegendByPlayerId(int playerID)
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

                    using (var cmd = new NpgsqlCommand(@"SELECT * FROM standings WHERE player_id = @Input", conn))
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
