using SengokuProvider.Library.Models.User;
using SengokuProvider.Library.Services.Comms.StartGG.Interfaces;
using SengokuProvider.Library.Services.Players;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SengokuProvider.Library.Services.Comms.StartGG
{
    public sealed class StartGgUserSyncService : IStartGgUserSyncService
    {
        private readonly IStartGgPlayerQueryService _startGgQueries;
        private readonly IPlayerQueryService _players;

        public StartGgUserSyncService(IStartGgPlayerQueryService startGgQueries, IPlayerQueryService players)
        {
            _startGgQueries = startGgQueries;
            _players = players;
        }

        public async Task<UserPlayerDataResponse> ResolveAsync(string playerName, string userSlug, CancellationToken cancellationToken = default)
        {
            var response = new UserPlayerDataResponse { Response = "Failed to find player data" };

            if (!string.IsNullOrWhiteSpace(userSlug))
            {
                var result = await _startGgQueries.GetUserAsync(userSlug, cancellationToken);
                if (result?.UserNode?.Player is null) return response;

                var existingPlayerId = await _players.GetPlayerIdByStartGgId(result.UserNode.Player.Id);
                response.Data = new UserPlayerData
                {
                    PlayerId = existingPlayerId > 0 ? existingPlayerId : result.UserNode.Player.Id,
                    PlayerName = result.UserNode.Player.GamerTag ?? string.Empty,
                    PlayerEmail = string.Empty,
                    userLink = result.UserNode.Id,
                    GameIds = []
                };
                response.Response = "Successfully retrieved user";
                return response;
            }

            if (!string.IsNullOrWhiteSpace(playerName))
            {
                var player = await _players.GetPlayerByName(playerName);
                response.Data = new UserPlayerData
                {
                    PlayerId = player.Id,
                    PlayerName = player.PlayerName,
                    PlayerEmail = string.Empty,
                    userLink = player.UserLink,
                    GameIds = []
                };
                response.Response = player.Id > 0 ? "Successfully retrieved user" : response.Response;
            }

            return response;
        }
    }
}
