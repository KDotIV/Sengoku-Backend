using GraphQL.Client.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SengokuProvider.Library.Services.Common;
using SengokuProvider.Library.Services.Comms.StartGG.Interfaces;
using System.Net;

namespace SengokuProvider.Library.Services.Comms.StartGG
{
    public class StartGgGraphQlClient : IStartGgGraphQlClient
    {
        private const int MaxRetries = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
        private readonly GraphQLHttpClient _client;
        private readonly RequestThrottler _throttler;

        public StartGgGraphQlClient(GraphQLHttpClient client, RequestThrottler throttler)
        {
            _client = client;
            _throttler = throttler;
        }

        public async Task<T?> QueryAsync<T>(string query, object variables, CancellationToken cancellationToken = default)
        {
            for (var attempt = 1; attempt <= MaxRetries; attempt++)
            {
                await _throttler.WaitIfPaused();
                try
                {
                    var response = await _client.SendQueryAsync<JObject>(new GraphQLHttpRequest
                    {
                        Query = query,
                        Variables = variables
                    }, cancellationToken);

                    if (response.Errors is { Length: > 0 })
                        throw new InvalidOperationException($"start.gg returned GraphQL errors: {string.Join(", ", response.Errors.Select(error => error.Message))}");
                    if (response.Data is null)
                        throw new InvalidOperationException("start.gg returned an empty response.");

                    return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(response.Data));
                }
                catch (GraphQLHttpRequestException exception) when (
                    exception.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable && attempt < MaxRetries)
                {
                    await _throttler.PauseRequests(_client);
                    await Task.Delay(RetryDelay, cancellationToken);
                }
            }

            throw new InvalidOperationException($"start.gg request failed after {MaxRetries} attempts.");
        }
    }
}
