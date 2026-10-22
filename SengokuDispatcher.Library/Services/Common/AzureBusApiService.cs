using Azure.Messaging.ServiceBus;
using SengokuDispatcher.Library.Services.Common.Interfaces;

namespace SengokuDispatcher.Library.Services.Common;

public sealed class AzureBusApiService(ServiceBusClient client, ILogger<AzureBusApiService> logger) : IAzureBusApiService
{
    public async Task<bool> SendBatchAsync(string? queueName, string messages)
    {
        if (string.IsNullOrWhiteSpace(queueName) || string.IsNullOrWhiteSpace(messages))
        {
            logger.LogWarning("Cannot publish a command without a queue and payload.");
            return false;
        }
        try
        {
            await using var sender = client.CreateSender(queueName);
            using var batch = await sender.CreateMessageBatchAsync();
            var message = new ServiceBusMessage(messages) { ContentType = "application/json" };
            if (!batch.TryAddMessage(message))
            {
                throw new InvalidOperationException("Command exceeds the Service Bus message size limit.");
            }
            await sender.SendMessagesAsync(batch);
            logger.LogInformation("Published command {MessageId} to {Queue}", message.MessageId, queueName);
            return true;
        }
        catch (ServiceBusException ex)
        {
            logger.LogError("Publishing to {Queue} failed ({Reason}, transient: {Transient})", queueName, ex.Reason, ex.IsTransient);
            throw;
        }
    }
}
