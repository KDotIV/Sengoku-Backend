using SengokuProvider.Library.Models.Legends;
using SengokuProvider.Library.Models.Players;

namespace SengokuProvider.Library.Services.Legends
{
    public interface ILegendQueryService
    {
        public Task<LegendData?> GetLegendsByPlayerLink(GetLegendsByPlayerLinkCommand getLegendsByPlayerLinkCommand);
        public Task<StandingsQueryResult?> QueryStandingsByPlayerId(int playerId);
    }
}