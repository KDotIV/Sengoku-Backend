using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Models.Legends
{
    public class OnboardReceivedData : IServiceBusCommand
    {
        public required LegendCommandRegistry Topic { get; set; }
        public required ICommand Command { get; set; }
        public required MessagePriority MessagePriority { get; set; }
    }
}
