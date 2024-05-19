namespace SengokuProvider.Library.Services.Users
{
    public interface IUserService
    {
        public Task<int> CreateUser(string username, string email, string password);
    }
}
