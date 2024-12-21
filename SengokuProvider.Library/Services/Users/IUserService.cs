using SengokuProvider.Library.Models.User;

namespace SengokuProvider.Library.Services.Users
{
    public interface IUserService
    {
        public Task<int> CreateUser(string username, string email, string password);
        public Task<UserData> GetUserById(int userId);
        public Task<bool> CheckUserById(int userId);
    }
}
