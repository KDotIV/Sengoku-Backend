using SengokuProvider.Library.Services.Events;

namespace SengokuProvider.Worker.Factories
{
    public interface IEventIntegrityFactory
    {
        public IEventIntegrityService CreateEventFactory();
    }
}