using SengokuProvider.Library.Services.Legends;

namespace SengokuProvider.Worker.Factories
{
    internal interface ILegendHandlerFactory
    {
        public ILegendIntegrityService CreateIntegrityHandler();
        public ILegendIntakeService CreateIntakeHandler();
        public ILegendQueryService CreateQueryHandler();
    }
}
