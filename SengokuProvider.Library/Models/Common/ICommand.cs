namespace SengokuProvider.Library.Models.Common
{
    public interface ICommand
    {
        public string? Response { get; set; }
        bool Validate();
    }
}
