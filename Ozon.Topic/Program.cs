using Bl.Extensions;
using Bl.Gates;
using Common.Configuration.Configs;
using DAL.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Ozon.Topic.Services;
using Serilog;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using LoggerConfiguration = Common.Configuration.LoggerConfiguration;

var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .Build();

Log.Logger = LoggerConfiguration.ConfigureLogger("brelki64-topic", configuration);

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
            services.Configure<PromtsConfig>(cfg.GetSection("Promts"));

            var token = cfg.GetValue<string>("BotConfig:Token");
            services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(token!));

            services.AddSingleton(Channel.CreateUnbounded<CallbackQuery>());

            services.UseDAL(cfg);
            services.UseBL();

            services.AddHostedService<TopicHostedService>();

            services.AddSingleton<IWebDriver>(sp =>
            {
                var opts = new ChromeOptions
                {
                    DebuggerAddress = "127.0.0.1:9224"
                };
                var relative = @"%LOCALAPPDATA%\Google\Chrome\topic_profile";
                opts.AddArgument("--user-data-dir=" + Environment.ExpandEnvironmentVariables(relative));

                return new ChromeDriver(opts);
            });
            services.AddSingleton<SeleniumGate>();
        })
        .Build();

    await host.RunAsync();
}
finally
{
    Log.CloseAndFlush();
}
