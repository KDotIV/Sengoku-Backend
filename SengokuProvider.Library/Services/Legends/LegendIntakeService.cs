using SengokuProvider.Library.Models.Legends;

namespace SengokuProvider.Library.Services.Legends
{
    public class LegendIntakeService : ILegendIntakeService
    {
        private readonly ILegendQueryService _legendQueryService;
        private readonly string _connectionString;
        public LegendIntakeService(string connectionString, ILegendQueryService queryService)
        {
            _connectionString = connectionString;
            _legendQueryService = queryService;
        }

        public async Task<LegendData?> GenerateNewLegends(int playerId)
        {
            Console.WriteLine("Beginning Onboarding Process...");

            var currentData = await _legendQueryService.QueryStandingsByPlayerId(playerId);

            return null;
        }
    }
}