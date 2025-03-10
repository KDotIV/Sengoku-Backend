using SengokuProvider.Library.Models.Common;

namespace SengokuProvider.Library.Models.Leagues
{
    public class OnboardTournamentToLeagueCommand : ICommand
    {
        public required int[] TournamentIds { get; set; }
        public required int LeagueId { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (TournamentIds.Length > 0 && LeagueId > 0) return true;
            return false;
        }
    }
    public class OnboardUserToLeagueCommand : ICommand
    {
        public required int PlayerId { get; set; }
        public required string PlayerName { get; set; }
        public required string PlayerEmail { get; set; }
        public required int LeagueId { get; set; }
        public required int[] GameIds { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            return PlayerId > 0 &&
                LeagueId > 0 &&
                !string.IsNullOrEmpty(PlayerName) &&
                !string.IsNullOrEmpty(PlayerEmail);
        }
    }
    public class OnboardPlayerToLeagueCommand : ICommand
    {
        public required int[] PlayerIds { get; set; }
        public required int LeagueId { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (PlayerIds.Length > 0 && LeagueId > 0) return true;
            return false;
        }
    }
    public class OnboardTournamentStandingstoLeague : ICommand
    {
        public required int[] TournamentLinks { get; set; }
        public required string EventLinkSlug { get; set; }
        public required int LeagueId { get; set; }
        public int[] GameIds { get; set; } = new int[0];
        public required bool Open { get; set; } = false;
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (TournamentLinks != null &&
                GameIds != null &&
                LeagueId > 0 &&
                !string.IsNullOrEmpty(EventLinkSlug)) return true;
            return false;
        }
    }
    public class GetLeaderboardResultsByLeagueCommand : ICommand
    {
        public required int LeagueId { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (LeagueId > 0) return true;
            return false;
        }
    }
    public class UpdateLeaderboardStandingsByLeague : ICommand
    {
        public required int[] LeagueIds { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (LeagueIds.Length > 0) return true;
            return false;
        }
    }
    public class GetLeaderboardsByOrgCommand : ICommand
    {
        public required int OrgId { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (OrgId > 0) return true;
            return false;
        }
    }
    public class CreateLeagueByOrgCommand : ICommand
    {
        public required string LeagueName { get; set; }
        public required int OrgId { get; set; }
        public required DateTime StartDate { get; set; }
        public required DateTime EndDate { get; set; }
        public int GameId { get; set; }
        public string? Description { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            if (!string.IsNullOrEmpty(LeagueName) &&
                !string.IsNullOrEmpty(LeagueName) &&
                OrgId > 0 && GameId > 0) return true;
            return false;
        }
    }
    public class CreateNewRunnerBoardCommand : ICommand
    {
        public required List<int> TournamentIds { get; set; }
        public required int UserId { get; set; }
        public required string UserName { get; set; }
        public int OrgId { get; set; } = 0;
        public string OrgName { get; set; } = "";
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }
        public bool Validate()
        {
            if (TournamentIds.Count > 0 && UserId > 0) return true;
            return false;
        }
    }
    public class AddTournamentToRunnerBoardCommand : ICommand
    {
        public required List<int> TournamentIds { get; set; }
        public required int UserId { get; set; }
        public required int OrgId { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }
        public bool Validate()
        {
            if (TournamentIds.Count > 0 && UserId > 0) return true;
            return false;
        }
    }
    public class GetCurrentRunnerBoardByUserCommand : ICommand
    {
        public required int UserId { get; set; }
        public CommandRegistry Topic { get; set; }
        public string? Response { get; set; }

        public bool Validate()
        {
            throw new NotImplementedException();
        }
    }
}
