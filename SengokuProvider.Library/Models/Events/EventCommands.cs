using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Models.Events
{
    public class IntakeEventsByLocationCommand : ICommand
    {
        public required int PerPage { get; set; }
        public string? StateCode { get; set; }
        public required int StartDate { get; set; }
        public required int EndDate { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }
        public bool Validate()
        {
            if (PerPage > 0 &&
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
        public required string Priority { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }
        public bool Validate()
        {
            if (RegionId > 0 &&
                !string.IsNullOrEmpty(Priority) &&
                PerPage > 0) return true;
            else return false;
        }
    }
    public class GetRelatedRegionsCommand : ICommand
    {
        public required int RegionId { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }
        public bool Validate()
        {
            if (RegionId > 0) return true;
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
        public required CommandRegistry Topic { get; set; }
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
    public class LinkTournamentByEventIdCommand : ICommand
    {
        public required int EventLinkId { get; set; }
        public required CommandRegistry Topic { get; set; }
        public string? Response { get; set; }
        public bool Validate()
        {
            if (EventLinkId > 0) return true;
            else return false;
        }
    }
    public class GetTournamentById : ICommand
    {
        public required int TournamentLinkId { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (TournamentLinkId > 0) { return true; }
            else return false;
        }
    }
    public class UpdateEventCommand : ICommand
    {
        public int EventId { get; set; }
        public required List<Tuple<string, string>> UpdateParameters { get; set; }
        public required CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (EventId > 0 &&
                UpdateParameters != null &&
                UpdateParameters.Count > 0) return true;
            else return false;
        }
    }
    public class GetCurrentBracketQueueByTournamentCommand : ICommand
    {
        public required int TournamentId { get; set; }
        public string? PoolName { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            throw new NotImplementedException();
        }
    }
}
