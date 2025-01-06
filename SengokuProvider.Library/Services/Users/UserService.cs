using Dapper;
using Npgsql;
using SengokuProvider.Library.Models.User;
using SengokuProvider.Library.Services.Common;

namespace SengokuProvider.Library.Services.Users
{
    public class UserService : IUserService
    {
        private readonly string? _connectionString;
        private readonly IntakeValidator _validator;
        private readonly Random _rand;
        public UserService(string? connectionString, IntakeValidator validator)
        {
            _connectionString = connectionString;
            _validator = validator;
        }
        public async Task<int> CreateUser(string username, string email, string password)
        {
            if (!_validator.IsValidIdentifier(username) || !_validator.IsValidIdentifier(email) || !_validator.IsValidIdentifier(password))
                throw new ArgumentException("Invalid input data");
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    if (CheckDuplicatedUser(email)) { throw new ArgumentException("Email is already in use"); }

                    var createNewUserCommand = @"INSERT INTO users (id, user_name, email, password) VALUES (@UserId, @Username, @Email, @Password) ON CONFLICT(id, email) DO NOTHING";
                    using (var command = new NpgsqlCommand(createNewUserCommand, conn))
                    {
                        command.Parameters.AddWithValue("@UserId", await GenerateNewUserId());
                        command.Parameters.AddWithValue("@Username", username);
                        command.Parameters.AddWithValue("@Email", email);
                        command.Parameters.AddWithValue("@Password", password);
                        var result = await command.ExecuteNonQueryAsync();
                        if (result > 0)
                            return result;
                        else
                            return 4; //4 = Failed
                    }
                }
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException("Database error occurred: ", ex);
            }
            catch (Exception ex)
            {
                throw new ApplicationException("Unexpected Error Occurred: ", ex);
            }
        }
        public async Task<UserData> GetUserById(int userId)
        {
            if (userId < 0) { throw new ArgumentNullException("Invalid UserId"); }
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
        private async Task<int> GenerateNewUserId()
        {
            using (var conn = new NpgsqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                while (true)
                {
                    var newId = _rand.Next(100000, 1000000);

                    var newQuery = @"SELECT id FROM users WHERE id = @Input";
                    var queryResult = await conn.QueryFirstOrDefaultAsync<int>(newQuery, new { Input = newId });
                    if (newId != queryResult || queryResult == 0) return newId;
                }
            }
        }
    }
}
