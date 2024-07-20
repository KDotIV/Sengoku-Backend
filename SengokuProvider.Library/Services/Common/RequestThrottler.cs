using GraphQL.Client.Http;
using Microsoft.Extensions.Configuration;
using SengokuProvider.Library.Models.Common;
using System.Net.Http.Headers;

namespace SengokuProvider.Library.Services.Common
{
    public class RequestThrottler
    {
        private bool _isPaused = false;
        private readonly IConfiguration _configuration;
        private readonly SemaphoreSlim _pauseSemaphore = new SemaphoreSlim(1, 3);
        private readonly TimeSpan _pauseDuration = TimeSpan.FromSeconds(5);
        private int _releaseLock;

        public RequestThrottler(IConfiguration config)
        {
            _configuration = config;
        }
        public async Task WaitIfPaused()
        {
            await _pauseSemaphore.WaitAsync();
            try
            {
                while (_isPaused)
                {
                    await Task.Delay(1000); // Check every second if the pause is lifted
                    if (_releaseLock < 5)
                    {
                        _releaseLock++;
                    }
                    else { _isPaused = false; break; }
                }
            }
            finally
            {
                _pauseSemaphore.Release();
            }
        }
        public async Task PauseRequests(GraphQLHttpClient currentClient)
        {
            await _pauseSemaphore.WaitAsync();
            try
            {
                _isPaused = true;
                Console.WriteLine("Changing Tokens");
                string currentToken = BearerConstants.TokenQueue.Dequeue();
                BearerConstants.TokenQueue.Enqueue(currentToken);

                currentClient.HttpClient.DefaultRequestHeaders.Clear();
                currentClient.HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", currentToken);
            }
            finally
            {
                _pauseSemaphore.Release();
            }

            await Task.Delay(_pauseDuration);

            await _pauseSemaphore.WaitAsync();
            try
            {
                _isPaused = false;
            }
            finally
            {
                _pauseSemaphore.Release();
            }
        }
    }
}
