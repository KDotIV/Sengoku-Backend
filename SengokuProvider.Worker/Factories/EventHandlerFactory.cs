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

        public IEventIntakeService CreateIntakeHandler()
        {
            var scope = _serviceScopeFactory.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IEventIntakeService>();
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
