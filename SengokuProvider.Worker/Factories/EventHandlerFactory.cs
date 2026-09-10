using SengokuProvider.Library.Services.Events;

namespace SengokuProvider.Worker.Factories
{
    public class EventHandlerFactory : IEventHandlerFactory
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public EventHandlerFactory(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }

        public SengokuProvider.Library.Workflows.Events.IEventOperations CreateIntakeHandler()
        {
            var scope = _serviceScopeFactory.CreateScope();
            return scope.ServiceProvider.GetRequiredService<SengokuProvider.Library.Workflows.Events.IEventOperations>();
        }

        public IEventIntegrityService CreateIntegrityHandler()
        {
            var scope = _serviceScopeFactory.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IEventIntegrityService>();
        }

        public IEventQueryService CreateQueryHandler()
        {
            var scope = _serviceScopeFactory.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IEventQueryService>();
        }
    }
}
