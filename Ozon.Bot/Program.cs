using Bl.Extensions;
using Bl.Gates;
using Common.Configuration.Configs;
using DAL.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Ozon.Bot.Services;
using Serilog;
using System.Threading.Channels;
using Telegram.Bot;
using Telegram.Bot.Types;
using LoggerConfiguration = Common.Configuration.LoggerConfiguration;
using Entities;
using Bl.Interfaces;
using Microsoft.Extensions.Options;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

Log.Logger = LoggerConfiguration.ConfigureLogger("brelki64-bot", configuration);

try
{
    var host = Host.CreateDefaultBuilder(args)
        .UseSerilog()
        .ConfigureServices((hostContext, services) =>
        {
            var cfg = hostContext.Configuration;

            services.Configure<ParserConfig>(cfg.GetSection("ParserConfig"));
            services.Configure<BotConfig>(cfg.GetSection("BotConfig"));
            services.Configure<ChatGptConfig>(cfg.GetSection("ChatGpt"));
            services.Configure<UrlsConfig>(cfg.GetSection("ThreadUrls"));
            services.Configure<PromtsConfig>(cfg.GetSection("Promts"));

            var token = cfg.GetValue<string>("BotConfig:Token");
            services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(token!));
            services.AddSingleton(Channel.CreateUnbounded<CallbackQuery>());

            services.UseDAL(cfg);
            services.UseBL();

            services.AddHostedService<BotHostedService>();
            services.AddHostedService<BotSchedulerHostedService>();
            services.AddHostedService<BotMenuHostedService>();

            services.AddSingleton<IWebDriver>(sp =>
            {
                var opts = new ChromeOptions
                {
                    DebuggerAddress = "127.0.0.1:9223"
                };
                var relative = @"%LOCALAPPDATA%\Google\Chrome\sender_profile";
                opts.AddArgument("--user-data-dir=" + Environment.ExpandEnvironmentVariables(relative));

                return new ChromeDriver(opts);
            });
            services.AddSingleton<SeleniumGate>();
        })
        .Build();

    // Инициализация ассистентов из конфига
    using (var scope = host.Services.CreateScope())
    {
        var cfg = scope.ServiceProvider.GetRequiredService<IOptions<ChatGptConfig>>().Value;
        var bl = scope.ServiceProvider.GetRequiredService<IAssistantDataBl>();
        foreach (var kv in cfg.Assistants)
        {
            await bl.AddOrUpdateAsync(new AssistantData(kv.Key, kv.Value));
        }
    }

    await host.RunAsync();
}
finally
{
    Log.CloseAndFlush();
}
