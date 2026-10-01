using SengokuProvider.Library.Services.Players;
using SengokuProvider.Library.Workflows.Players;

namespace SengokuProvider.Worker.Factories
{
    public interface IPlayerHandlerFactory
    {
        public IPlayerOperations CreateIntakeHandler();
        public IPlayerIntegrityService CreateIntegrityHandler();
        public IPlayerQueryService CreateQueryHandler();
    }
}
