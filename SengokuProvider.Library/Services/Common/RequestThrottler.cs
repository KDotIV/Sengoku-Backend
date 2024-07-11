namespace SengokuProvider.Library.Services.Common
{
    public class RequestThrottler
    {
        private bool _isPaused = false;
        private readonly SemaphoreSlim _pauseSemaphore = new SemaphoreSlim(1, 1);
        private readonly TimeSpan _pauseDuration = TimeSpan.FromSeconds(5);
        private int _releaseLock;

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
        public async Task PauseRequests()
        {
            await _pauseSemaphore.WaitAsync();
            try
            {
                _isPaused = true;
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
