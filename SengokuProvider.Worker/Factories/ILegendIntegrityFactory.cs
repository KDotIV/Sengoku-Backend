using SengokuProvider.Library.Services.Legends;

namespace SengokuProvider.Worker.Factories
{
    public interface ILegendIntegrityFactory
    {
        public ILegendIntegrityService CreateLegendFactory();
    }
}