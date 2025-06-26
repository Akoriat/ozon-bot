using Telegram.Bot.Types;

namespace Bl.Interfaces
{
    public interface IBotService
    {
        public Task DoScheduledWorkAsync(CancellationToken stoppingToken);
        public Task HandleUpdateAsync(Update update, CancellationToken cancellationToken);
        public Task ProcessCallbackInBackgroundAsync(CallbackQuery callbackQuery, CancellationToken ct);
        public Task ManualTriggerRefreshAsync();
    }
}
