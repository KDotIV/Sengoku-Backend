using Dapper;
using Npgsql;
using SengokuProvider.Library.Models.User;
using SengokuProvider.Library.Services.Common;

namespace SengokuProvider.Library.Services.Users
{
    public partial class UserService : IUserService
    {
        private readonly string _connectionString;
        private readonly IntakeValidator _validator;
        private readonly Random _rand = new Random();
        public UserService(string connectionString, IntakeValidator validator)
        {
            _connectionString = connectionString;
            _validator = validator;
        }
        public async Task<int> CreateUser(string username, string email, string password, int playerId = 0)
        {
            if (!_validator.IsValidIdentifier(username) || !_validator.IsValidIdentifier(email) || !_validator.IsValidIdentifier(password))
                throw new ArgumentException("Invalid input data");
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    if (CheckDuplicatedUser(email)) { throw new ArgumentException("Email is already in use"); }

                    var userId = await GenerateNewUserId();
                    var createNewUserCommand = @"INSERT INTO users (id, user_name, email, password, player_id, user_link) VALUES (@UserId, @Username, @Email, @Password, @PlayerId, @UserLink) ON CONFLICT(email) DO NOTHING";
                    using (var command = new NpgsqlCommand(createNewUserCommand, conn))
                    {
                        command.Parameters.AddWithValue("@UserId", userId);
                        command.Parameters.AddWithValue("@Username", username);
                        command.Parameters.AddWithValue("@Email", email);
                        command.Parameters.AddWithValue("@Password", password);
                        command.Parameters.AddWithValue("@PlayerId", playerId);
                        command.Parameters.AddWithValue("@UserLink", 0);
                        var result = await command.ExecuteNonQueryAsync();
                        if (result > 0)
                            return userId;
                        else
                            return 0; // No row inserted.
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
        public async Task<UserData?> GetUserById(int userId)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userId);
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                return await conn.QuerySingleOrDefaultAsync<UserData>(
                    @"SELECT id, user_name AS UserName, email, password,
                             player_id AS PlayerId, user_link AS UserLink
                      FROM users WHERE id = @userId", new { userId });
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException("Failed to retrieve the user from PostgreSQL.", ex);
            }
        }

        public async Task<bool> CheckUserById(int userId)
        {
            if (userId <= 0) return false;
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync();
                return await conn.ExecuteScalarAsync<bool>(
                    "SELECT EXISTS (SELECT 1 FROM users WHERE id = @userId)", new { userId });
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException("Failed to check the user in PostgreSQL.", ex);
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
