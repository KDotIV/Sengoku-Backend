namespace SengokuDispatcher.Library.Services.Common.Interfaces
{
    public interface IAzureBusApiService
    {
        public Task<bool> SendBatchAsync(string? queueName, string messages);
    }
}
