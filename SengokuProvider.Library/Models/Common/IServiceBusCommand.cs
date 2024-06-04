namespace SengokuProvider.Library.Models.Common
{
    public interface IServiceBusCommand
    {
        public ICommand Command { get; set; }
        public MessagePriority MessagePriority { get; set; }
    }
    public enum MessagePriority
    {
        OnDemand,
        SystemIntake,
        UserIntake,
        Background
    }
}