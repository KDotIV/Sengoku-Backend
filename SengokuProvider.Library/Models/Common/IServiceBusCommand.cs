namespace SengokuProvider.Library.Models.Common
{
    public interface IServiceBusCommand
    {
        public ICommand Command { get; set; }
        public Topic Topic { get; set; }
        public MessagePriority MessagePriority { get; set; }
    }
    public enum Topic
    {
        Events,
        Players,
        Legends
    }
    public enum MessagePriority
    {
        OnDemand,
        SystemIntake,
        UserIntake,
        Background
    }
}