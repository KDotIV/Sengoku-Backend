using SengokuProvider.Library.Models.Common;

namespace SengokuDispatcher.Library.Models.Messaging
{
    public class OnboardReceivedData : IServiceBusCommand
    {
        public required ICommand Command { get; set; }
        public required MessagePriority MessagePriority { get; set; }
    }
}
