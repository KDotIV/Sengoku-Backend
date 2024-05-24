using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Models.Players
{
    public class IntakePlayersByTournamentCommand : ICommand
    {
        public required string EventSlug { get; set; }
        public required int PerPage { get; set; }
        public required int PageNum { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (!string.IsNullOrWhiteSpace(EventSlug))
                return true;
            return false;
        }
    }
    public class GetPlayerStandingsCommand : ICommand
    {
        public required int EventId { get; set; }
        public required int PerPage { get; set; }
        public required string GamerTag { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (EventId > 0 &&
                PerPage > 0 &&
                string.IsNullOrEmpty(GamerTag)) return true;
            else return false;
        }
    }
}
