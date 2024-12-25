using Dapper;
using GraphQL.Client.Http;
using Npgsql;
using NpgsqlTypes;
using SengokuProvider.Library.Models.Orgs;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;
using SengokuProvider.Library.Services.Users;

namespace SengokuProvider.Library.Services.Orgs
{
    public class OrganizerIntakeService : IOrganizerIntakeService
    {
        private readonly string _connectionString;
        private readonly GraphQLHttpClient _client;
        private readonly RequestThrottler _throttler;
        private static Random _rand = new Random();
        private readonly IUserService _userService;
        private readonly ICommonDatabaseService _commonDatabaseService;
        public OrganizerIntakeService(string connectionString, GraphQLHttpClient client, RequestThrottler throttler, 
            IUserService userService, ICommonDatabaseService commonServices)
        {
            _connectionString = connectionString;
            _client = client;
            _throttler = throttler;
            _userService = userService;
            _commonDatabaseService = commonServices;
        }

        public Task AddBracketToRun(int[] tournamentIds, int userId)
        {
            throw new NotImplementedException();
        }
        public async Task<bool> CreateTravelCoOp(CreateTravelCoOpCommand command)
        {
            if(command == null || command.UserId < 0 ) throw new ArgumentNullException(nameof(command));
            if (await _userService.CheckUserById(command.UserId) == false) throw new ArgumentException($"User must already be registered");

            return await InsertNewCoOpData(command.UserId, command.OperationName, command.UserName, command.FundingGoal,
                command.CurrentItems, command.CurrentFunding, command.CollabUserIds);
        }
        private async Task<bool> InsertNewCoOpData(int userId, string OpName, string userName, double fundingGoal, 
            List<int> currentItems, double currentFunding, List<int> collabUserIds)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"INSERT INTO co_ops (id, op_name, op_owner, funding_goal, 
                                                    current_funding, current_items, owner_userid, payledger_id, collab_userids, last_updated) 
                                                    VALUES (@InputId, @InputOpName, @InputOwnerName, @InputFundingGoal, 
                                                    @CurrentFunding, @CurrentItems, @OwnerUserId, @PayLedger, @CurrentCollab, @LastUpdated)
                                                    ON CONFLICT (id) DO UPDATE SET
                                                        op_name = EXCLUDED.op_name, op_owner = EXCLUDED.op_owner, 
                                                        funding_goal = EXCLUDED.funding_goal, current_funding = EXCLUDED.current_funding, 
                                                        current_items = EXCLUDED.current_items, owner_userid = EXCLUDED.owner_userid, payledger_id = EXCLUDED.payledger_id,
                                                        last_updated = EXCLUDED.last_updated", conn))
                    {
                        cmd.Parameters.AddWithValue(@"InputId", NpgsqlDbType.Integer, await GenerateNewCoOpId());
                        cmd.Parameters.AddWithValue(@"InputOpName", OpName);
                        cmd.Parameters.AddWithValue(@"InputOwnerName", userName);
                        cmd.Parameters.Add(_commonDatabaseService.CreateDBNumericType("InputFundingGoal", fundingGoal));
                        cmd.Parameters.Add(_commonDatabaseService.CreateDBNumericType("CurrentFunding", currentFunding));
                        cmd.Parameters.Add(_commonDatabaseService.CreateDBIntArrayType("CurrentItems", [.. currentItems]));
                        cmd.Parameters.Add(_commonDatabaseService.CreateDBIntArrayType("CurrentCollab", [.. collabUserIds]));
                        cmd.Parameters.AddWithValue(@"OwnerUserId", userId);
                        cmd.Parameters.AddWithValue(@"PayLedger", 100);
                        cmd.Parameters.AddWithValue(@"LastUpdated", DateTime.UtcNow);

                        var result = await cmd.ExecuteNonQueryAsync();
                        if (result > 0) return true;
                    }
                }
                return false;
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
        public Task DeleteBracketFromCurrentRun(int[] tournamentIds, int userId)
        {
            throw new NotImplementedException();
        }
        public async Task StartBracketRun(int[] tournamentIds, int userId)
        {
            if (tournamentIds.Length == 0 || userId == 0) return;
        }

        public Task UpdateBracketFromCurrentRun(int[] tournamentIds, int userId)
        {
            throw new NotImplementedException();
        }
        private async Task<int> GenerateNewCoOpId()
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    while (true)
                    {
                        var newId = _rand.Next(100000, 1000000);

                        var newQuery = @"SELECT id FROM co_ops where id = @Input";
                        var queryResult = await conn.QueryFirstOrDefaultAsync<int>(newQuery, new { Input = newId });
                        if (newId != queryResult || queryResult == 0) return newId;
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
    }
}
