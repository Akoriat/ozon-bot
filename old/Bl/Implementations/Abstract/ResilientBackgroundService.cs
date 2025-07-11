using Microsoft.Extensions.Hosting;

namespace Bl.Implementations.Abstract
{
    public abstract class ResilientBackgroundService : BackgroundService
    {
        private readonly TimeSpan _restartDelay = TimeSpan.FromSeconds(5);

        protected ResilientBackgroundService() { }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunServiceAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    try
                    {
                        await Task.Delay(_restartDelay, stoppingToken);
                    }
                    catch (TaskCanceledException) { /* отмена */ }
                }
            }
        }

        protected abstract Task RunServiceAsync(CancellationToken stoppingToken);
    }
}
