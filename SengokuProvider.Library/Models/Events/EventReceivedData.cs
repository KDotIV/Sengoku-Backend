using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Models.Events
{
    public class EventReceivedData : IServiceBusCommand
    {
        public required MessagePriority MessagePriority { get; set; }
        public required ICommand Command { get; set; }
    }
}
