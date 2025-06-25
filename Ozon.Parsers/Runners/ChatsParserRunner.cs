using Bl.Implementations;
using Bl.Interfaces;
using DAL.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Bl.Common.Configs;
using Bl.Common.Enum;

namespace Ozon.Parsers.Runners
{
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
}