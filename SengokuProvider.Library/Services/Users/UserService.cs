using Dapper;
using Npgsql;
using SengokuProvider.Library.Models.User;
using SengokuProvider.Library.Services.Common;

namespace SengokuProvider.Library.Services.Users
{
    public class UserService : IUserService
    {
        private readonly string _connectionString;
        private readonly IntakeValidator _validator;
        public UserService(string connectionString, IntakeValidator validator)
        {
            _connectionString = connectionString;
            _validator = validator;
        }
        public async Task<int> CreateUser(string username, string email, string password)
        {
            if (!_validator.IsValidIdentifier(username) || !_validator.IsValidIdentifier(email) || !_validator.IsValidIdentifier(password))
            {
                throw new ArgumentException("Invalid input data");
            }
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                if (CheckDuplicatedUser(email)) { throw new ArgumentException("Email is already in use"); }

                var createNewUserCommand = @"INSERT INTO users (user_name, email, password) VALUES (@Username, @Email, @Password)";
                using (var command = new NpgsqlCommand(createNewUserCommand, conn))
                {
                    command.Parameters.AddWithValue("@Username", username);
                    command.Parameters.AddWithValue("@Email", email);
                    command.Parameters.AddWithValue("@Password", password);
                    return await command.ExecuteNonQueryAsync();
                }
            }
        }
        public async Task<UserData> GetUserById(int userId)
        {
            if(userId < 0) { throw new ArgumentNullException("Invalid UserId"); }
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                var newQuery = @"SELECT * FROM users WHERE id = @Input";
                var result = await conn.QueryFirstOrDefaultAsync(newQuery, new { Input = userId });

                return result != null;
            };
        }
        public async Task<bool> CheckUserById(int userId)
        {
            if (userId < 0) return false;
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();

                var newQuery = @"SELECT * FROM users WHERE id = @Input";
                return await conn.QueryFirstOrDefaultAsync<bool>(newQuery, new { Input = userId });
            }
        }
        private bool CheckDuplicatedUser(string input)
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                conn.Open();

                var newQuery = @"SELECT email FROM users WHERE email = @Input";
                var result = conn.QueryFirstOrDefault<string>(newQuery, new { Input = input });

                return result != null;
            }
        }
    }
}
