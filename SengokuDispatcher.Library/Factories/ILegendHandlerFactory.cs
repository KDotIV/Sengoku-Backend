using SengokuProvider.Library.Services.Legends;

namespace SengokuDispatcher.Library.Factories
{
    public interface ILegendHandlerFactory
    {
        public ILegendIntegrityService CreateIntegrityHandler();
        public ILegendIntakeService CreateIntakeHandler();
        public ILegendQueryService CreateQueryHandler();
    }
}
