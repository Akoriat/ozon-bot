using Bl.Common.Configs;
using Bl.Implementations.Abstract;
using Bl.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Channels;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Ozon.Bot.Services
{
    public class BotHostedService : ResilientBackgroundService
    {
        private readonly IBotService _botService;
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<BotHostedService> _logger;
        private readonly Channel<CallbackQuery> _callbackQueue;
        private readonly long _forumChatId;

        public BotHostedService(
            IBotService botService,
            ITelegramBotClient botClient,
            IChatParserBl chatParserBl,
            ILogger<BotHostedService> logger,
            IOptions<BotConfig> options,
            Channel<CallbackQuery> callbackQueue)
        {
            _botService = botService;
            _botClient = botClient;
            _logger = logger;
            _forumChatId = options.Value.ForumChatId;
            _callbackQueue = callbackQueue;
        }

        protected override async Task RunServiceAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"Запуск Telegram Bot Hosted Service {DateTime.UtcNow}");

            var receiverOptions = new ReceiverOptions
            {
                DropPendingUpdates = true,
                AllowedUpdates = Array.Empty<UpdateType>()
            };

            var chatId = new ChatId(_forumChatId);
            var scope = new BotCommandScopeChat() { ChatId = chatId };
            await _botClient.DeleteMyCommands(
        scope: scope,   // или любой другой scope
        languageCode: ""                       // "" = все языки
);

            _botClient.StartReceiving(
                updateHandler: async (client, update, ct) =>
                {
                    _logger.LogInformation("Получен апдейт типа {UpdateType}", update.Type);
                    try
                    {
                        await _botService.HandleUpdateAsync(update, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при обработке апдейта");
                    }
                },
                errorHandler: (client, exception, ct) =>
                {
                    _logger.LogError(exception, "Ошибка в получении апдейтов");
                    return Task.CompletedTask;
                },
                receiverOptions: receiverOptions,
                cancellationToken: stoppingToken
            );

            _ = Task.Run(async () =>
                {
                    await foreach (var cq in _callbackQueue.Reader.ReadAllAsync(stoppingToken))
                    {
                        try
                        {
                            await _botService.ProcessCallbackInBackgroundAsync(cq, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Ошибка фоновой обработки callback");
                        }
                    }
                }, stoppingToken);

            await Task.Delay(Timeout.Infinite, stoppingToken);
            return;
        }

    }



}
