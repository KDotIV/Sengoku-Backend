using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Players;
using SengokuProvider.Library.Models.User;

namespace SengokuProvider.Library.Services.Players
{
    public interface IPlayerQueryService
    {
        public Task<List<PlayerStandingResult>> GetPlayerStandingResults(GetPlayerStandingsCommand command);
        public Task<List<PlayerData>> GetRegisteredPlayersByTournamentId(int[] tournamentIds);
        public Task<UserPlayerData> GetUserDataByUserLink(int userLink);
        public Task<UserPlayerData> GetUserDataByPlayerName(string playerName);
        public Task<List<PlayerTournamentCard>> GetTournamentCardsByPlayerIDs(int[] playerIds);
        public Task<List<PlayerStandingResult>> GetStandingsDataByPlayerIds(int[] playerIds, int[] tournamentIds);
        public Task<List<Links>> GetPlayersByEntrantLinks(int[] entrantId);
        public Task<int> GetPlayerIdByStartGgId(int playerLinkId);
        public Task<PlayerData> GetPlayerByName(string playerName);
    }
}
