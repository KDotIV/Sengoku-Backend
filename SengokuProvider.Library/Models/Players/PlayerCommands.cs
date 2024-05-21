using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Models.Players
{
    public class IntakePlayersByTournamentCommand : ICommand
    {
        public required string EventSlug { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (!string.IsNullOrWhiteSpace(EventSlug))
                return true;
            return false;
        }
    }
}
