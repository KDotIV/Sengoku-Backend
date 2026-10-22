using SengokuProvider.Library.Services.Players;
using SengokuDispatcher.Library.Workflows.Players;

namespace SengokuDispatcher.Library.Factories
{
    public interface IPlayerHandlerFactory
    {
        public IPlayerOperations CreateIntakeHandler();
        public IPlayerIntegrityService CreateIntegrityHandler();
        public IPlayerQueryService CreateQueryHandler();
    }
}
