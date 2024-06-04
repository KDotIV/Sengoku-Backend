using SengokuProvider.Library.Services.Events;

namespace SengokuProvider.Worker.Factories
{
    public interface IEventHandlerFactory
    {
        public IEventIntegrityService CreateEventFactory();
    }
}