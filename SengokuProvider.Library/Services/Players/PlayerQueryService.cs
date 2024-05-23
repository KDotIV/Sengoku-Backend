using GraphQL.Client.Http;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using SengokuProvider.Library.Models.Events;
using SengokuProvider.Library.Models.Players;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SengokuProvider.Library.Services.Players
{
    public class PlayerQueryService : IPlayerQueryService
    {
        private readonly GraphQLHttpClient _client;

        private readonly string _connectionString;
        public PlayerQueryService(string connectionString, GraphQLHttpClient graphQlClient)
        {

            _connectionString = connectionString;
            _client = graphQlClient;
        }
        public async Task<PlayerGraphQLResult> GetPlayerDataFromStartgg(IntakePlayersByTournamentCommand queryCommand)
        {
            return await QueryStartggPlayerData(queryCommand);
        }
        private async Task<PlayerGraphQLResult> QueryStartggPlayerData(IntakePlayersByTournamentCommand command)
        {
            var tempQuery = @"query EventEntrants($perPage: Int!, $eventSlug: String!, $pageNum: Int) {
                                  event(slug: $eventSlug) {
                                    id
                                    name
                                    entrants(query: {perPage: $perPage, page: $pageNum filter: {}}) {
                                      nodes {
                                        id
                                        participants {id player{id gamerTag}}
                                        standing {id placement}}}}}";
            var request = new GraphQLHttpRequest
            {
                Query = tempQuery,
                Variables= new
                {
                    command.EventSlug,
                    command.PerPage,
                    command.PageNum
                }
            };
            try
            {
                var response = await _client.SendQueryAsync<JObject>(request);
                if (response.Data == null) throw new ApplicationException($"Failed to retrieve player data");

                var tempJson = JsonConvert.SerializeObject(response.Data, Formatting.Indented);

                var playerData = JsonConvert.DeserializeObject<PlayerGraphQLResult>(tempJson);
                return playerData;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message + ": " + ex.StackTrace);
                throw;
            }
        }
    }
}
