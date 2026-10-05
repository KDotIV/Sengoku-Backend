using GraphQL.Client.Http;
using Microsoft.Extensions.Configuration;
using SengokuProvider.Library.Models.Common;
using System.Net.Http.Headers;

namespace SengokuProvider.Library.Services.Common
{
    public class RequestThrottler
    {
        private readonly SemaphoreSlim _pauseSemaphore = new(1, 1);
        private readonly TimeSpan _pauseDuration = TimeSpan.FromSeconds(5);

        public RequestThrottler(IConfiguration config)
        {
        }

        public async Task WaitIfPaused()
        {
            await _pauseSemaphore.WaitAsync();
            _pauseSemaphore.Release();
        }

        public async Task PauseRequests(GraphQLHttpClient currentClient, IReadOnlySet<string>? exhaustedTokens = null)
        {
            await _pauseSemaphore.WaitAsync();
            try
            {
                var currentToken = currentClient.HttpClient.DefaultRequestHeaders.Authorization?.Parameter;
                // The queue is shared across throttler instances. Skip the active
                // token so a rotation actually selects a different bearer.
                lock (BearerConstants.TokenQueue)
                {
                    var candidates = BearerConstants.TokenQueue.Count;
                    for (var i = 0; i < candidates; i++)
                    {
                        var nextToken = BearerConstants.TokenQueue.Dequeue();
                        BearerConstants.TokenQueue.Enqueue(nextToken);
                        if (string.IsNullOrWhiteSpace(nextToken) || nextToken == currentToken ||
                            exhaustedTokens?.Contains(nextToken) == true) continue;
                        currentClient.HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", nextToken);
                        break;
                    }
                }
                // Hold the gate throughout the cooldown. Waiters cannot release
                // the pause early, and exceptions always release the gate.
                await Task.Delay(_pauseDuration);
            }
            finally
            {
                _pauseSemaphore.Release();
            }
        }
    }
}
