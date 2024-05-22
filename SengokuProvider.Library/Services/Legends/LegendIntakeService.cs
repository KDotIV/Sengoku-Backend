using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }
}
