using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Worker.Handlers
{
    public class PlayerReceivedData : IServiceBusCommand
    {
        public required ICommand Command { get; set; }
        public required MessagePriority MessagePriority { get; set; }
    }
}