using Bl.Common.Configs;
using Bl.Extensions;
using DAL.Extensions;
using DocumentFormat.OpenXml.Office2016.Drawing.ChartDrawing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Ozon.Bot.Services;
using Ozon.Parsers;
using Serilog;
using System;
using Telegram.Bot;
using Telegram.Bot.Types;
using TL;

var builder = WebApplication.CreateBuilder(args);

var configuration = new ConfigurationBuilder()
   .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
   .Build();

builder.Services.Configure<ParserConfig>(configuration.GetSection("ParserConfig"));
builder.Services.Configure<BotConfig>(configuration.GetSection("BotConfig"));
builder.Services.Configure<SellerConfig>(configuration.GetSection("SellerName"));
var token = configuration.GetSection("BotConfig").GetValue<string>("Token");
builder.Services.UseDAL(configuration);
builder.Services.UseBL();

builder.Host.UseSerilog(Common.Configuration.LoggerConfiguration.ConfigureLogger("brelki64-parsers", configuration));


builder.Services.AddHostedService<ParsersAggregatorService>();

builder.Services.AddSingleton<ITelegramBotClient>(_ => new TelegramBotClient(token));

builder.Services.AddSingleton<IWebDriver>(sp =>
{
    var opts = new ChromeOptions
    {
        DebuggerAddress = "127.0.0.1:9222"
    };
    var relative = @"%LOCALAPPDATA%\Google\Chrome\parser_profile";
    opts.AddArgument("--user-data-dir=" + Environment.ExpandEnvironmentVariables(relative));

    return new ChromeDriver(opts);
});

builder.Services.AddSingleton<SeleniumGate>();

var app = builder.Build();
app.Run();