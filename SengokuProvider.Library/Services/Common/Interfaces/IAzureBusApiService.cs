namespace SengokuProvider.Library.Services.Common.Interfaces
{
    public interface IAzureBusApiService
    {
        public Task<bool> SendBatchAsync(string? queueName, string messages);
        public Task SendAsync(string queueName, string message, string messageId, CancellationToken cancellationToken = default);
    }
}
