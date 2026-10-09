using SengokuProvider.Library.Models.User;

namespace SengokuProvider.Library.Services.Users;

public interface IUserService
{
    Task<int> CreateUser(string username, string email, string password, int playerId = 0);
    Task<UserData?> GetUserById(int userId);
    Task<bool> CheckUserById(int userId);
    Task<StartggProfileLink> SaveStartggProfile(int userId, int playerId, CommonUserNode profile, CancellationToken cancellationToken = default);
}
