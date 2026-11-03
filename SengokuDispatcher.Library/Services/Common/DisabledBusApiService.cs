using SengokuDispatcher.Library.Services.Common.Interfaces;

namespace SengokuDispatcher.Library.Services.Common;

public sealed class DisabledBusApiService : IAzureBusApiService
{
    public Task<bool> SendBatchAsync(string? queueName, string messages)
    {
        throw new InvalidOperationException("Service Bus dispatch is disabled. Enable Dispatcher:ServiceBusEnabled before submitting intake jobs.");
    }
}
