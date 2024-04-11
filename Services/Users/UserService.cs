using Dapper;
using Npgsql;
using System.Text.RegularExpressions;

namespace SengokuProvider.API.Services.Users
{
    public class UserService : IUserService
    {
        private readonly string _connectionString;
        public UserService(string connectionString)
        {
            _connectionString = connectionString;
        }
        public async Task<int> CreateUser(string username, string email, string password)
        {
            if (!IsValidIdentifier(username) || !IsValidIdentifier(email) || !IsValidIdentifier(password))
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
        private bool IsValidIdentifier(string input)
        {
            // Basic check for common SQL injection patterns
            if (string.IsNullOrWhiteSpace(input) || input.Contains(';') ||
                input.Contains("'") || input.Contains("--") ||
                input.Contains("/*") || input.Contains("*/"))
            {
                return false;
            }

            // Regular expression to validate the input
            var regex = new Regex("^[a-zA-Z0-9_@.-]+$");
            return regex.IsMatch(input);
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
