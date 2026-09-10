using SengokuProvider.Library.Services.Players;

namespace SengokuProvider.Worker.Factories
{
    public interface IPlayerHandlerFactory
    {
        public SengokuProvider.Library.Workflows.Players.IPlayerOperations CreateIntakeHandler();
        public IPlayerIntegrityService CreateIntegrityHandler();
        public IPlayerQueryService CreateQueryHandler();
    }
}
