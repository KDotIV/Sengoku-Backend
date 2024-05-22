using GraphQL.Client.Http;
using Npgsql;
using SengokuProvider.Library.Models.Legends;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        public async Task<LegendData> GetLegendsByPlayerLink(GetLegendsByPlayerLinkCommand command)
        {
            return await QueryLegendsByPlayerLink(command.PlayerLinkId);
        }

        private async Task<LegendData> QueryLegendsByPlayerLink(int playerLinkId)
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
