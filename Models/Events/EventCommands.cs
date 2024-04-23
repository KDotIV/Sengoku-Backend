
using SengokuProvider.API.Models.Common;

namespace SengokuProvider.API.Models.Events
{
    public class IntakeEventsByLocationCommand : ICommand
    {
        public required int Page { get; set; }
        public string? StateCode { get; set; }
        public required int StartDate { get; set; }
        public required int EndDate { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (Page > 0 &&
                !string.IsNullOrEmpty(StateCode) &&
                StartDate > 0 &&
                EndDate > 0)
                return true;
            else return false;
        }
    }
    public class GetTournamentsByLocationCommand : ICommand
    {
        public required int RegionId { get; set; }
        public required int PerPage { get; set; }
        public bool Sorted { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (RegionId > 0 &&
                PerPage > 0) return true;
            else return false;
        }
    }
    public class IntakeEventsByGameIdCommand : ICommand
    {
        public required int Page { get; set; }
        public string? StateCode { get; set; }
        public required int StartDate { get; set; }
        public required int EndDate { get; set; }
        public required int[] GameIDs { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (Page > 0 &&
                !string.IsNullOrEmpty(StateCode) &&
                StartDate > 0 &&
                EndDate > 0)
                return true;
            else return false;
        }
    }
    public class IntakeEventsByTournamentIdCommand : ICommand
    {
        public required int TournamentId { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (TournamentId > 0) return true;
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
            if (EventId > 0 &&
                UpdateParameters != null &&
                UpdateParameters.Count > 0) return true;
            else return false;
        }
    }
}
