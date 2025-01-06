using Dapper;
using GraphQL.Client.Http;
using Npgsql;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Orgs;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Common.Interfaces;

namespace SengokuProvider.Library.Services.Orgs
{
    public class OrganizerQueryService : IOrganizerQueryService
    {
        private readonly string? _connectionString;
        private readonly GraphQLHttpClient? _client;
        private readonly RequestThrottler? _throttler;
        private readonly ICommonDatabaseService? _commonDatabaseService;
        public OrganizerQueryService(string? connectionString, GraphQLHttpClient? httpClient, RequestThrottler? throttler,
            ICommonDatabaseService? commonServices)
        {
            _connectionString = connectionString;
            _client = httpClient;
            _throttler = throttler;
            _commonDatabaseService = commonServices;
        }
        public async Task<List<TravelCoOpResult>> GetCoOpResultsByUserId(int userId)
        {
            if (userId < 0) { throw new ArgumentOutOfRangeException(nameof(userId)); }

            return await GetCoOpDataByUserId(userId);
        }

        private async Task<List<TravelCoOpResult>> GetCoOpDataByUserId(int userId)
        {
            var results = new List<TravelCoOpResult>();
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();
                    using (var cmd = new NpgsqlCommand(@"Select * FROM co_ops WHERE owner_userid = @UserInput OR collab_userids && @InputArray", conn))
                    {
                        cmd.Parameters.AddWithValue("@UserInput", userId);
                        cmd.Parameters.Add(_commonDatabaseService.CreateDBIntArrayType("@InputArray", new[] { userId }));

                        using (var reader = await cmd.ExecuteReaderAsync())
                        {
                            if (!reader.HasRows) return results;
                            while (await reader.ReadAsync())
                            {
                                SqlMapper.AddTypeHandler(new GenericArrayHandler<int>());

                                var newTravelCoOp = new TravelCoOpResult
                                {
                                    UserName = reader.GetString(reader.GetOrdinal("op_owner")),
                                    UserId = reader.GetInt32(reader.GetOrdinal("owner_userid")),
                                    OperationName = reader.GetString(reader.GetOrdinal("op_name")),
                                    FundingGoal = reader.GetDouble(reader.GetOrdinal("funding_goal")),
                                    CurrentFunding = reader.GetDouble(reader.GetOrdinal("current_funding")),
                                    CoOpItems = reader.GetFieldValue<int[]>(reader.GetOrdinal("current_items")).ToList(),
                                    CollabUserIds = reader.GetFieldValue<int[]>(reader.GetOrdinal("collab_userids")).ToList(),
                                    LastUpdated = reader.GetDateTime(reader.GetOrdinal("last_updated"))
                                };
                                results.Add(newTravelCoOp);
                            }
                        }
                    }
                }
                return results;
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
        public Task DisplayCurrentBracketQueue(int bracketQueueId)
        {
            throw new NotImplementedException();
        }

        public Task<List<BracketQueueData>> GetBracketQueueByTournamentId(int tournamentId)
        {
            throw new NotImplementedException();
        }

        public Task GetCurrentTournamentStations(int orgId)
        {
            throw new NotImplementedException();
        }
    }
}
