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
            if(PlayerId > 0 &&
                !string.IsNullOrEmpty(GamerTag)) return true;
            return false;
        }
    }
    public class OnboardLegendsByPlayerLinkCommand : ICommand
    {
        public Guid? OperationId { get; set; }
        public required int[] PlayerLinkIds { get; set; }
        public required CommandRegistry Topic { get; set; }
        public string? Response { get; set; }
        public bool Validate()
        {
            if (PlayerLinkIds != null && PlayerLinkIds.Length > 0 && PlayerLinkIds.All(id => id > 0)) return true;
            return false;
        }
    }
}
