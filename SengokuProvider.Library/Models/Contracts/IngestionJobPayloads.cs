using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SengokuProvider.Library.Models.Contracts
{
    public sealed record ImportEventsByLocationPayload(
        int PerPage,
        string StateCode,
        int StartDate,
        int EndDate,
        string[] Filters,
        string[] VariableDefinitions);

    public sealed record ImportEventsByGamePayload(
        int Page,
        string StateCode,
        int StartDate,
        int EndDate,
        int[] GameIds);

    public sealed record LinkTournamentByEventPayload(int EventLinkId);

    public sealed record ImportPlayersByTournamentPayload(int TournamentLink);

    public sealed record ImportPlayerHistoryPayload(int PlayerId, string GamerTag, int PerPage);

    public sealed record BuildBracketSnapshotPayload(string BracketSlug, int PlayerId);

    public sealed record LinkStartGgUserPayload(string PlayerName, string UserSlug);

    public sealed record ImportTournamentStandingsToLeaguePayload(
        int[] TournamentLinks,
        string EventLinkSlug,
        int[] GameIds,
        int LeagueId,
        bool Open);
}
