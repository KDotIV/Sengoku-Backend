using SengokuProvider.Library.Services.Events;

namespace SengokuProvider.Worker.Factories
{
    public class EventIntegrityFactory : IEventIntegrityFactory
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public EventIntegrityFactory(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }
        public IEventIntegrityService CreateEventFactory()
        {
            var scope = _serviceScopeFactory.CreateScope();
            return scope.ServiceProvider.GetRequiredService<IEventIntegrityService>();
        }
    }
}
