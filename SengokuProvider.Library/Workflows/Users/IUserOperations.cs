using SengokuProvider.Library.Models.User;

namespace SengokuProvider.Library.Workflows.Users;

public interface IUserOperations
{
    Task<StartggProfileLink> LinkStartggProfileBySlug(int userId, int playerId, string urlSlug, CancellationToken cancellationToken = default);
    Task<StartggProfileLink> LinkStartggProfileByUserId(int userId, int playerId, int startggUserId, CancellationToken cancellationToken = default);
    Task<UserPlayerDataResponse> SyncStartggDataToUserData(string playerName, string userSlug);
}
