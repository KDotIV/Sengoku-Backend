namespace SengokuProvider.Library.Services.Legends
{
    public interface ILegendIntegrityService
    {
        public Task<List<int>> BeginLegendIntegrity();
    }
}
