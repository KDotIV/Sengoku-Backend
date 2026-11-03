using Azure.Messaging.ServiceBus;
using Sengoku.Observability;

namespace SengokuDispatcher.Library.Handlers;

internal static class MessageOperation
{
    public static async Task Run(OperationLogger logger, string title, ProcessMessageEventArgs args, Func<Task> action)
    {
        var traceParent = args.Message.ApplicationProperties.TryGetValue("traceparent", out var parent) ? parent as string : null;
        using var operation = logger.Begin(title, OperationFields.Of(("MessageId", args.Message.MessageId),
            ("DeliveryCount", args.Message.DeliveryCount)), traceParent, forceNew: true);
        try { await action(); operation.Complete(); }
        catch (Exception ex) { operation.Fail(ex); throw; }
    }
}
