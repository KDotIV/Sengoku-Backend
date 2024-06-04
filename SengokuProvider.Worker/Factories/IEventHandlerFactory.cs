using SengokuProvider.Library.Services.Events;

namespace SengokuProvider.Worker.Factories
{
    public interface IEventHandlerFactory
    {
        public IEventIntegrityService CreateIntegrityHandler();
        public IEventIntakeService CreateIntakeHandler();
        public IEventQueryService CreateQueryHandler();
    }
}