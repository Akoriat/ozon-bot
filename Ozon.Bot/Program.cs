using Bl.Common.Configs;
using Bl.Extensions;
using DAL.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Ozon.Bot.Services;
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
            services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(token));
            services.AddSingleton(Channel.CreateUnbounded<CallbackQuery>());

            services.UseDAL(cfg);
            services.UseBL();

            services.AddHostedService<BotHostedService>();
            services.AddHostedService<BotSchedulerHostedService>();

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
            //services.AddSingleton(_ =>
            //{
            //    var options = new ChromeOptions
            //    {
            //        DebuggerAddress = "127.0.0.1:9223"
            //    };
            //    var relative = @"%LOCALAPPDATA%\Google\Chrome\sender_profile";
            //    options.AddArgument("--user-data-dir=" + Environment.ExpandEnvironmentVariables(relative));
            //    return options;
            //});
            //services.AddTransient<IWebDriver>(sp =>
            //    new ChromeDriver(sp.GetRequiredService<ChromeOptions>())
            //);
        })
        .Build();

    await host.RunAsync();
}
finally
{
    Log.CloseAndFlush();
}
