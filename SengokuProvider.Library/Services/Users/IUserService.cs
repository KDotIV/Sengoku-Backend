using SengokuProvider.Library.Models.User;

namespace SengokuProvider.Library.Services.Users
{
    public interface IUserService
    {
        public Task<int> CreateUser(string username, string email, string password, int playerId = 0);
        public Task<UserData> GetUserById(int userId);
        public Task<bool> CheckUserById(int userId);
        public Task<UserPlayerDataResponse> SyncStartggDataToUserData(string playerName, string userSlug);
    }
}
