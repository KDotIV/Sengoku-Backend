namespace SengokuProvider.Library.Models.Common
{
    public interface ICommand
    {
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }
        bool Validate();
    }
}
