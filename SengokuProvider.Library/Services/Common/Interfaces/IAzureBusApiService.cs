namespace SengokuProvider.Library.Services.Common.Interfaces
{
    public interface IAzureBusApiService
    {
        public Task<bool> SendBatchAsync(string? queueName, string messages);
    }
}
