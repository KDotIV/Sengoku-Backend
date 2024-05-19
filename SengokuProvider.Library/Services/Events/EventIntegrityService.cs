using Dapper;
using GraphQL.Client.Http;
using Npgsql;
using SengokuProvider.Library.Models.Events;

namespace SengokuProvider.Library.Services.Events
{
    public class EventIntegrityService : IEventIntegrityService
    {
        private readonly GraphQLHttpClient _client;
        private readonly string _connectionString;
        public EventIntegrityService(GraphQLHttpClient client, string connectionString)
        {
            _client = client;
            _connectionString = connectionString;
        }
        public async Task<List<int>> BeginIntegrityTournamentLinks()
        {
            return await GetTournamentLinksToProcess();
        }
        public async Task<bool> VerifyTournamentLinkChange(int linkId)
        {
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var newQuery = @"Select url_slug, games_ids from tournament_links
                                   WHERE id = @Input";
                    var link = await conn.QueryFirstOrDefaultAsync<TournamentData>(newQuery, new { Input = linkId });
                    if (link == null || link.Games == null ||
                        link.Games.Length == 0 || string.IsNullOrEmpty(link.UrlSlug))
                    {
                        return false;
                    }
                }
                return true;
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.StackTrace}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error While Processing: {ex.Message} - {ex.StackTrace}");
            }
            return false;
        }
        private async Task<List<int>> GetTournamentLinksToProcess()
        {
            var linksToProcess = new List<int>();
            try
            {
                using (var conn = new NpgsqlConnection(_connectionString))
                {
                    await conn.OpenAsync();

                    var newQuery = @"Select id from tournament_links
                                   WHERE url_slug IS NULL and games_ids IS NULL;";
                    using (var reader = await conn.ExecuteReaderAsync(newQuery))
                    {
                        while (await reader.ReadAsync())
                        {
                            linksToProcess.Add(reader.GetInt32(0));
                        }
                    }
                }
                return linksToProcess;
            }
            catch (NpgsqlException ex)
            {
                throw new ApplicationException($"Database error occurred: {ex.StackTrace}", ex);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error While Processing: {ex.Message} - {ex.StackTrace}");
            }
            return linksToProcess;
        }
    }
}
