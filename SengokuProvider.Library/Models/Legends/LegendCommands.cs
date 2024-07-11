using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Models.Legends
{
    public class GetLegendsByPlayerLinkCommand : ICommand
    {
        public required int PlayerLinkId { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (PlayerLinkId != 0) return true;
            return false;
        }
    }
    public class OnboardLegendsByPlayerCommand : ICommand
    {
        public required int PlayerId { get; set; }
        public required string GamerTag { get; set; }
        public required CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            throw new NotImplementedException();
        }
    }
}
