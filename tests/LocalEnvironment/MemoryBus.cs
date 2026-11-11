using System.Collections.Concurrent;
using System.Threading.Channels;
using Azure.Messaging.ServiceBus;

namespace LocalEnvironment;

// Test-only SDK doubles. Production workers, serialization, outbox and settlement paths remain real.
// Does not simulate AMQP, broker locks, network failures or Azure identity/RBAC.
public sealed class MemoryBus : ServiceBusClient
{
    private readonly ConcurrentDictionary<string, Channel<ServiceBusMessage>> _queues = new();
    public ConcurrentQueue<string> DeadLetters { get; } = new();
    public int Published;
    public int Completed;
    public bool Available = true;
    private Channel<ServiceBusMessage> Queue(string name) => _queues.GetOrAdd(name, _ => Channel.CreateUnbounded<ServiceBusMessage>());
    public override ServiceBusSender CreateSender(string queueName) => new Sender(this, queueName);
    public override ServiceBusReceiver CreateReceiver(string queueName) => Available
        ? new Receiver(this, queueName) : throw new InvalidOperationException("Local broker probe unavailable");
    public override ServiceBusProcessor CreateProcessor(string queueName, ServiceBusProcessorOptions options) => new Processor(this, queueName);
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class Sender(MemoryBus bus, string queue) : ServiceBusSender
    {
        private readonly Dictionary<ServiceBusMessageBatch, IList<ServiceBusMessage>> _batches = new();
        public override ValueTask<ServiceBusMessageBatch> CreateMessageBatchAsync(CancellationToken cancellationToken = default)
        {
            var messages = new List<ServiceBusMessage>();
            var batch = ServiceBusModelFactory.ServiceBusMessageBatch(0, messages);
            _batches.Add(batch, messages);
            return ValueTask.FromResult(batch);
        }
        public override Task SendMessagesAsync(ServiceBusMessageBatch batch, CancellationToken cancellationToken = default)
        {
            var messages = _batches[batch];
            _batches.Remove(batch);
            return SendMessagesAsync(messages, cancellationToken);
        }
        public override async Task SendMessagesAsync(IEnumerable<ServiceBusMessage> messages, CancellationToken cancellationToken = default)
        {
            foreach (var message in messages)
            {
                await bus.Queue(queue).Writer.WriteAsync(message, cancellationToken);
                Interlocked.Increment(ref bus.Published);
            }
        }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Receiver(MemoryBus bus, string queue) : ServiceBusReceiver
    {
        public override string EntityPath => queue;
        public override Task<IReadOnlyList<ServiceBusReceivedMessage>> PeekMessagesAsync(int maxMessages, long? fromSequenceNumber = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ServiceBusReceivedMessage>>([]);
        public override Task CompleteMessageAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref bus.Completed); return Task.CompletedTask; }
        public override Task RenewMessageLockAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public override Task DeadLetterMessageAsync(ServiceBusReceivedMessage message, string deadLetterReason, string? deadLetterErrorDescription = null, CancellationToken cancellationToken = default)
        { bus.DeadLetters.Enqueue(queue + ": " + deadLetterReason); return Task.CompletedTask; }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Processor(MemoryBus bus, string queue) : ServiceBusProcessor
    {
        private CancellationTokenSource? _stop;
        private Task? _loop;
        public override Task StartProcessingAsync(CancellationToken cancellationToken = default)
        {
            _stop = new CancellationTokenSource();
            _loop = Task.Run(async () =>
            {
                try
                {
                    await foreach (var message in bus.Queue(queue).Reader.ReadAllAsync(_stop.Token))
                    {
                        var received = ServiceBusModelFactory.ServiceBusReceivedMessage(body: message.Body,
                            messageId: message.MessageId, subject: message.Subject,
                            properties: message.ApplicationProperties.ToDictionary(pair => pair.Key, pair => pair.Value));
                        await OnProcessMessageAsync(new ProcessMessageEventArgs(received, new Receiver(bus, queue), _stop.Token));
                    }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
                catch (Exception ex) { bus.DeadLetters.Enqueue(queue + ": " + ex.GetType().Name); }
            });
            return Task.CompletedTask;
        }
        public override async Task StopProcessingAsync(CancellationToken cancellationToken = default)
        {
            _stop?.Cancel();
            if (_loop != null) await _loop.WaitAsync(cancellationToken);
        }
        public override async Task CloseAsync(CancellationToken cancellationToken = default)
        { await StopProcessingAsync(cancellationToken); _stop?.Dispose(); _stop = null; }
    }
}
