using SengokuProvider.Library.Services.Legends;

namespace SengokuDispatcher.Library.Factories
{
    public class LegendHandlerFactory : ILegendHandlerFactory
    {
        private readonly IServiceProvider _services;

        public LegendHandlerFactory(IServiceProvider serviceFactory)
        {
            _services = serviceFactory;
        }
        public ILegendIntakeService CreateIntakeHandler()
        {
            return _services.GetRequiredService<ILegendIntakeService>();
        }

        public ILegendIntegrityService CreateIntegrityHandler()
        {
            return _services.GetRequiredService<ILegendIntegrityService>();
        }

        public ILegendQueryService CreateQueryHandler()
        {
            return _services.GetRequiredService<ILegendQueryService>();
        }
    }
}
