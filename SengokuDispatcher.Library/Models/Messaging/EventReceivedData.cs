using SengokuProvider.Library.Models.Common;

namespace SengokuDispatcher.Library.Models.Messaging
{
    public class EventReceivedData : IServiceBusCommand
    {
        public required MessagePriority MessagePriority { get; set; }
        public required ICommand Command { get; set; }
    }
}
