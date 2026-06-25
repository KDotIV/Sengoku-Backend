using SengokuProvider.Library.Models.User;

namespace SengokuProvider.Library.Services.Comms.StartGG
{
    public interface IStartGgUserSyncService
    {
        Task<UserPlayerDataResponse> ResolveAsync(string playerName, string userSlug, CancellationToken cancellationToken = default);
    }
}