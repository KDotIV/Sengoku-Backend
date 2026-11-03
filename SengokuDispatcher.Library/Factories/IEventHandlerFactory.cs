using SengokuDispatcher.Library.Services.Events;
using SengokuProvider.Library.Services.Events;
using SengokuDispatcher.Library.Workflows.Events;

namespace SengokuDispatcher.Library.Factories
{
    public interface IEventHandlerFactory
    {
        public IEventIntegrityService CreateIntegrityHandler();
        public IEventOperations CreateIntakeHandler();
        public IEventQueryService CreateQueryHandler();
    }
}
