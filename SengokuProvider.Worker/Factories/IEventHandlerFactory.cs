using SengokuProvider.Library.Services.Events;
using SengokuProvider.Library.Workflows.Events;

namespace SengokuProvider.Worker.Factories
{
    public interface IEventHandlerFactory
    {
        public IEventIntegrityService CreateIntegrityHandler();
        public IEventOperations CreateIntakeHandler();
        public IEventQueryService CreateQueryHandler();
    }
}
