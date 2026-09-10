using SengokuProvider.Library.Services.Events;

namespace SengokuProvider.Worker.Factories
{
    public interface IEventHandlerFactory
    {
        public IEventIntegrityService CreateIntegrityHandler();
        public SengokuProvider.Library.Workflows.Events.IEventOperations CreateIntakeHandler();
        public IEventQueryService CreateQueryHandler();
    }
}
