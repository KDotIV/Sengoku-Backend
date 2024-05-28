using SengokuProvider.Library.Services.Players;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SengokuProvider.Worker.Factories
{
    internal class PlayerIntegrityFactory : IPlayerIntegrityFactory
    {
        private readonly IServiceScopeFactory _factoryScope;
        public PlayerIntegrityFactory(IServiceScopeFactory serviceScopeFactory)
        {
            _factoryScope = serviceScopeFactory;
        }
        public IPlayerIntegrityService CreatePlayerFactory()
        {
            var scope = _factoryScope.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IPlayerIntegrityService>();
        }
    }
}
