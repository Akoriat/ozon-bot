using Bl.Implementations.Abstract;
using Bl.Interfaces;
using Common.Configuration.Configs;
using Common.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ozon.Parsers.Runners;

namespace Ozon.Parsers;

public class ParsersAggregatorService : ResilientBackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IParsersModeBl _parsersModeBl;
    private readonly ILogger<ParsersAggregatorService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    public ParsersAggregatorService(
        ILogger<ParsersAggregatorService> logger,
        IServiceProvider serviceProvider,
        IParsersModeBl parsersModeBl)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _parsersModeBl = parsersModeBl;
    }

    protected override async Task RunServiceAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ParsersAggregatorService запущен. Парсеры и топики каждые {Min} минут.", _interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {

                    var config = scope.ServiceProvider.GetRequiredService<IOptions<ParserConfig>>().Value;
                    await RunAllParsers(scope.ServiceProvider, config, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в ParsersAggregatorService");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("ParsersAggregatorService остановлен.");
    }

    private async Task RunAllParsers(IServiceProvider sp, ParserConfig config, CancellationToken ct)
    {
        var modes = await _parsersModeBl.GetAllModesAsync(ct);
        var chatParser = modes.Where(x => x.ParserName == ParserType.ChatParserApp.ToString()).First().IsActive;
        var reviewParser = modes.Where(x => x.ParserName == ParserType.ReviewsParser.ToString()).First().IsActive;
        var questionParser = modes.Where(x => x.ParserName == ParserType.QuestionsParserApp.ToString()).First().IsActive;

        if (chatParser)
        {
            _logger.LogInformation("Запуск ChatParser...");
            ChatsParserRunner.RunChatParserAsync(sp, config, ct);
        }
        if (questionParser)
        {
            _logger.LogInformation("Запуск OzonSellerParser...");
            QuestionsParserRunner.RunQuestionsParserAsync(sp, config, ct);
        }
        if (reviewParser)
        {
            _logger.LogInformation("Запуск ReviewsParser...");
            ReviewsParserRunner.RunReviewsParserApp(sp, config, ct);
        }

        _logger.LogInformation("Все парсеры отработали.");
    }
}
