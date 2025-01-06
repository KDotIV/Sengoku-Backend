using Azure.Messaging.ServiceBus;
using SengokuProvider.Library.Services.Common.Interfaces;

namespace SengokuProvider.Library.Services.Common
{
    public class AzureBusApiService : IAzureBusApiService
    {
        private readonly ServiceBusClient? _client;
        public AzureBusApiService(ServiceBusClient? client)
        {
            _client = client;
        }
        public async Task<bool> SendBatchAsync(string? queueName, string messages)
        {
            if (string.IsNullOrEmpty(queueName) || string.IsNullOrEmpty(messages)) return false;

            ServiceBusSender sender = _client.CreateSender(queueName);
            ServiceBusMessageBatch batch = await sender.CreateMessageBatchAsync();

            ServiceBusMessage busMessage = new ServiceBusMessage(messages)
            {
                ContentType = "application/json",
            };
            if (!batch.TryAddMessage(busMessage))
            {
                throw new InvalidOperationException("Message too large for batch.");
            }
            await sender.SendMessagesAsync(batch);
            await sender.DisposeAsync();

            return true;
        }
    }
}
