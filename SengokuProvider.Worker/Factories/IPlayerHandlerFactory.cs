using SengokuProvider.Library.Services.Players;

namespace SengokuProvider.Worker.Factories
{
    public interface IPlayerHandlerFactory
    {
        public IPlayerIntakeService CreateIntakeHandler();
        public IPlayerIntegrityService CreateIntegrityHandler();
        public IPlayerQueryService CreateQueryHandler();
    }
}