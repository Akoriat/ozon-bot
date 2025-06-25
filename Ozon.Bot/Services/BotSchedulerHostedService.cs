using Bl.Implementations.Abstract;
using Bl.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Globalization;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace Ozon.Bot.Services;

public sealed class BotSchedulerHostedService : ResilientBackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BotSchedulerHostedService> _logger;
    private readonly List<TimeOnly> _runTimes;

    public BotSchedulerHostedService(
        IConfiguration configuration,
        IServiceProvider services,
        ILogger<BotSchedulerHostedService> logger)
    {
        _services = services;
        _logger = logger;

        _runTimes = configuration.GetSection("BotSchedule")
                                 .Get<string[]>()
                                 ?.Select(t => TimeOnly.ParseExact(t, "HH:mm", CultureInfo.InvariantCulture))
                                 .OrderBy(t => t)
                                 .ToList()
                     ?? throw new InvalidOperationException("Отсутствует секция BotSchedule в appsettings.json");
    }

    protected override async Task RunServiceAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BotScheduler запущен; ежедневные вызовы в {Times}.",
                               string.Join(", ", _runTimes));

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = GetDelayUntilNextRun();
            _logger.LogInformation("Следующий запуск через {Delay}.", delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            //try
            //{
            using var scope = _services.CreateScope();
            var botService = scope.ServiceProvider.GetRequiredService<IBotService>();

            //    await botService.DoScheduledWorkAsync(stoppingToken);
            //    _logger.LogInformation("Запланированная задача выполнена успешно.");
            //}
            //catch (Exception ex)
            //{
            //    _logger.LogError(ex, "Ошибка при выполнении запланированной задачи");
            //}
            // вместо прямого await DoScheduledWork — триггерим тот же семафорный метод
            botService.ManualTriggerRefresh();
            _logger.LogInformation("Запланированный Refresh запущен (или пропущен, если уже идёт).");
        }
    }

    private TimeSpan GetDelayUntilNextRun()
    {
        var now = TimeOnly.FromDateTime(DateTime.Now);
        var next = _runTimes.FirstOrDefault(t => t > now) != TimeOnly.MinValue
                   ? _runTimes.FirstOrDefault(t => t > now) : _runTimes.First();

        var nextDateTime = DateTime.Today.Add(next.ToTimeSpan());
        if (nextDateTime < DateTime.Now)
            nextDateTime = nextDateTime.AddDays(1);

        return nextDateTime - DateTime.Now;
    }

}
