using SengokuProvider.Library.Services.Legends;

namespace SengokuProvider.Worker.Factories
{
    internal class LegendHandlerFactory : ILegendHandlerFactory
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public LegendHandlerFactory(IServiceScopeFactory serviceFactory)
        {
            _serviceScopeFactory = serviceFactory;
        }
        public ILegendIntakeService CreateIntakeHandler()
        {
            var scope = _serviceScopeFactory.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ILegendIntakeService>();
        }

        public ILegendIntegrityService CreateIntegrityHandler()
        {
            var scope = _serviceScopeFactory.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ILegendIntegrityService>();
        }

        public ILegendQueryService CreateQueryHandler()
        {
            var scope = _serviceScopeFactory.CreateScope();
            return scope.ServiceProvider.GetRequiredService<ILegendQueryService>();
        }
    }
}
