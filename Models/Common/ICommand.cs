namespace SengokuProvider.API.Models.Common
{
    public interface ICommand
    {
        public string TableName { get; set; }
        public string? Response { get; set; }
        bool Validate();
    }
}
