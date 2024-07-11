using Npgsql;
using SengokuProvider.Library.Models.Common;
using SengokuProvider.Library.Models.Legends;

namespace SengokuProvider.Library.Services.Legends
{
    public class LegendIntegrityService : ILegendIntegrityService
    {
        private readonly ILegendIntakeService _intakeService;
        private readonly ILegendQueryService _queryService;
        private readonly string _connectionString;
        public LegendIntegrityService(string connectionString, ILegendQueryService legendQueryService, ILegendIntakeService legendIntakeService)
        {
            _connectionString = connectionString;
            _intakeService = legendIntakeService;
            _queryService = legendQueryService;
        }
        public async Task<List<OnboardLegendsByPlayerCommand>> BeginLegendIntegrity()
        {
            return await GetLegendsToUpdate();
        }
        private async Task<List<OnboardLegendsByPlayerCommand>> GetLegendsToUpdate()
        {
            return await GetPlayersByLastUpdated();
        }
        private async Task<List<OnboardLegendsByPlayerCommand>> GetPlayersByLastUpdated()
        {
            List<OnboardLegendsByPlayerCommand> playersResult = new List<OnboardLegendsByPlayerCommand>();
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    using (var cmd = new NpgsqlCommand(@"SELECT p.id, p.player_name FROM public.players AS p
                                                        LEFT JOIN public.legends AS l ON l.player_id = p.id
                                                        WHERE l.last_updated < CURRENT_DATE OR l.last_updated IS NULL;", conn))
                    {
                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var newCommand = new OnboardLegendsByPlayerCommand
                                {
                                    PlayerId = reader.GetInt32(reader.GetOrdinal("id")),
                                    GamerTag = reader.GetString(reader.GetOrdinal("player_name")),
                                    Topic = CommandRegistry.OnboardLegendsByPlayerData
                                };
                                playersResult.Add(newCommand);
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
            return playersResult;
        }
    }
}
