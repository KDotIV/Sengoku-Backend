
using SengokuProvider.API.Models.Common;

namespace SengokuProvider.API.Models.Events
{
    public class TournamentIntakeCommand : ICommand
    {
        public required int Page { get; set; }
        public required string StateCode { get; set; }
        public required int StartDate { get; set; }
        public required int EndDate { get; set; }
        public required string[] Filters { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (Page > 0 &&
                !string.IsNullOrEmpty(StateCode) &&
                StartDate > 0 &&
                EndDate > 0 &&
                Filters.Length > 0)
                return true;
            else return false;
        }
    }
    public class UpdateEventCommand : ICommand
    {
        public required int EventId { get; set; }
        public required List<Tuple<string, string>> UpdateParameters { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            throw new NotImplementedException();
        }
    }
}
