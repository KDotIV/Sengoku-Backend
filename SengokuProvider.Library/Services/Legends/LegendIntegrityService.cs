using SengokuProvider.Library.Services.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public async Task<List<int>> BeginLegendIntegrity()
        {
            return await GetLegendsToUpdate();
        }

        private async Task<List<int>> GetLegendsToUpdate()
        {
            throw new NotImplementedException();
        }
    }
}
