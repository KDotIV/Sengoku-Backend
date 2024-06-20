using SengokuProvider.Library.Models.Legends;

namespace SengokuProvider.Library.Services.Legends
{
    public interface ILegendIntegrityService
    {
        public Task<List<OnboardLegendsByPlayerCommand>> BeginLegendIntegrity();
    }
}
