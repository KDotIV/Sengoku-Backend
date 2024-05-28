using SengokuProvider.Library.Services.Legends;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SengokuProvider.Worker.Factories
{
    internal class LegendIntegrityFactory : ILegendIntegrityFactory
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        public LegendIntegrityFactory(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }
        public ILegendIntegrityService CreateLegendFactory()
        {
            var scope = _serviceScopeFactory.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ILegendIntegrityService>();
        }
    }
}
