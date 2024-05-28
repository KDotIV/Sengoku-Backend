using SengokuProvider.Library.Services.Players;

namespace SengokuProvider.Worker.Factories
{
    public interface IPlayerIntegrityFactory
    {
        public IPlayerIntegrityService CreatePlayerFactory();
    }
}
