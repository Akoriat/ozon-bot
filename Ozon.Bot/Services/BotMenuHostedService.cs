using Bl.Implementations.Abstract;
using Bl.Interfaces;
using Common.Enums;
using Entities.DTOs;
using Microsoft.Extensions.Configuration;

namespace Ozon.Bot.Services;

internal class BotMenuHostedService : ResilientBackgroundService
{
    private readonly IBotService _botService;
    private readonly ILastMessageIdFromGeneralBl _lastMessageBl;
    private readonly int _menuInterval;
    public BotMenuHostedService(IBotService botService, IConfiguration configuration, ILastMessageIdFromGeneralBl lastMessageBl)
    {
        _botService = botService;
        _menuInterval = configuration.GetValue<int>("MenuTimeInterval");
        _lastMessageBl = lastMessageBl;
    }
    protected override async Task RunServiceAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!await _botService.CheckIfLastMessageIsMenuAsync(stoppingToken))
            {
                var messageId = await _botService.InitializeBotMenuAsync(stoppingToken);
                await _lastMessageBl.AddOrUpdateAsync(new LastMessageDto { LastMessageId = messageId, LastMessageType = LastMessageType.Menu });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при инициализации меню бота: {ex.Message}");
            throw;
        }

        await Task.Delay(TimeSpan.FromMinutes(_menuInterval), stoppingToken);
    }
}
