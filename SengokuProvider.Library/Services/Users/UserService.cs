using Dapper;
using Npgsql;
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
