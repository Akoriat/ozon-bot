using Bl.Interfaces;
using Common.Configuration.Configs;
using Common.Enums;
using DAL.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Ozon.Parsers.Runners;

public static class ChatsParserRunner
{
    public static void RunChatParserAsync(IServiceProvider serviceProvider, ParserConfig config, CancellationToken cancellationToken)
    {
        var parser = serviceProvider.GetRequiredService<IChatParserService>();
        var dataStore = serviceProvider.GetRequiredService<IChatParserBl>();
        var newDataRepository = serviceProvider.GetRequiredService<INewDataRepositoryBl>();
        var dateLimitBl = serviceProvider.GetRequiredService<IParserDateLimitBl>();

        try
        {
            // Навигация
            string url = config.ChatParserSiteUrl;
            parser.Navigate(url);

            //var newestDbDate = await dataStore.GetNewestChatDate();

            var chatIds = dataStore.GetLatestChatIds();

            var limit = dateLimitBl.GetStopDateAsync(ParserType.ChatParserApp.ToString(), cancellationToken).GetAwaiter().GetResult();
            var data = parser.ExtractNewChats(chatIds, limit);

            var updateData = parser.UpdateChats(limit);

            data.UnionWith(updateData);

            foreach (var chatId in data)
            {
                var existing = newDataRepository.GetEntryBySourceRecordId(chatId);
                if (existing != null)
                    continue;

                var newEntry = new NewDataEntry
                {
                    ParserName = "ChatParserApp",
                    SourceRecordId = chatId,
                    CreatedAt = DateTime.UtcNow,
                    Processed = false
                };
                newDataRepository.AddEntry(newEntry);
            }

            Console.WriteLine("Парсинг завершён. Данные сохранены в PostgreSQL.");
        }
        finally
        {
        }
    }
}