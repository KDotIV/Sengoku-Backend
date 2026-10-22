using SengokuDispatcher.Library.Services.Events;
using SengokuProvider.Library.Services.Events;
using SengokuDispatcher.Library.Workflows.Events;

namespace SengokuDispatcher.Library.Factories
{
    public class EventHandlerFactory : IEventHandlerFactory
    {
        private readonly IServiceProvider _services;
        public EventHandlerFactory(IServiceProvider services)
        {
            _services = services;
        }

        public IEventOperations CreateIntakeHandler()
        {
            return _services.GetRequiredService<IEventOperations>();
        }

        public IEventIntegrityService CreateIntegrityHandler()
        {
            return _services.GetRequiredService<IEventIntegrityService>();
        }

        public IEventQueryService CreateQueryHandler()
        {
            return _services.GetRequiredService<IEventQueryService>();
        }
    }
}
