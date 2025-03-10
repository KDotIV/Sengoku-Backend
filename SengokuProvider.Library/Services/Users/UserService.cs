using Dapper;
using Npgsql;
using SengokuProvider.Library.Models.User;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Players;

namespace SengokuProvider.Library.Services.Users
{
    public class UserService : IUserService
    {
        private readonly IPlayerQueryService _playerQueryService;
        private readonly string _connectionString;
        private readonly IntakeValidator _validator;
        private readonly Random _rand = new Random();
        public UserService(string connectionString, IntakeValidator validator, IPlayerQueryService playerQuery)
        {
            _connectionString = connectionString;
            _validator = validator;
            _playerQueryService = playerQuery;
        }
        public async Task<int> CreateUser(string username, string email, string password, int playerId = 0)
        {
            if (!_validator.IsValidIdentifier(username) || !_validator.IsValidIdentifier(email) || !_validator.IsValidIdentifier(password))
                throw new ArgumentException("Invalid input data");
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    conn.Open();

                    if (CheckDuplicatedUser(email)) { throw new ArgumentException("Email is already in use"); }

                    var createNewUserCommand = @"INSERT INTO users (id, user_name, email, password, player_id, user_link) VALUES (@UserId, @Username, @Email, @Password, @PlayerId, @UserLink) ON CONFLICT(email) DO NOTHING";
                    using (var command = new NpgsqlCommand(createNewUserCommand, conn))
                    {
                        command.Parameters.AddWithValue("@UserId", await GenerateNewUserId());
                        command.Parameters.AddWithValue("@Username", username);
                        command.Parameters.AddWithValue("@Email", email);
                        command.Parameters.AddWithValue("@Password", password);
                        command.Parameters.AddWithValue("@PlayerId", playerId);
                        command.Parameters.AddWithValue("@UserLink", 0);
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
            }
            ;
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
        public async Task<UserPlayerDataResponse> SyncStartggDataToUserData(string playerName, string userSlug)
        {
            var currentResponse = new UserPlayerDataResponse { Response = "" };
            try
            {
                if (!string.IsNullOrEmpty(userSlug))
                {
                    currentResponse.Data = await _playerQueryService.GetUserDataByUserSlug(userSlug);

                    if (currentResponse.Data.PlayerId == 0) { currentResponse.Response = "Failed to Retrieve User"; return currentResponse; }
                    else
                    {
                        currentResponse.Response = "Successfully Retrieved User";
                        return currentResponse;
                    }
                }
                else if (!string.IsNullOrEmpty(playerName))
                {
                    currentResponse.Data = await _playerQueryService.GetUserDataByPlayerName(playerName);
                    if (currentResponse.Data.PlayerId == 0) { currentResponse.Response = "Failed to Retrieve User"; return currentResponse; }
                    else
                    {
                        currentResponse.Response = "Successfully Retrieved User";
                        return currentResponse;
                    }
                }
                else
                {
                    currentResponse.Response = "Failed to find Player Data";
                    return currentResponse;
                }
            }
            catch (Exception)
            {

                throw;
            }
        }
    }
}
