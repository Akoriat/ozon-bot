using Bl.Interfaces;
using Common.Configuration.Configs;
using Common.Enums;
using DAL.Models;
using Entities.DTOs;
using Microsoft.Extensions.DependencyInjection;

namespace Ozon.Parsers.Runners;

public static class QuestionsParserRunner
{
    public static void RunQuestionsParserAsync(IServiceProvider serviceProvider, ParserConfig config, CancellationToken cancellationToken)
    {
        var parser = serviceProvider.GetRequiredService<IQuestionParserService>();
        var dataStore = serviceProvider.GetRequiredService<IQuestionDataStoreBl>();
        var newDataRepository = serviceProvider.GetRequiredService<INewDataRepositoryBl>();
        var dateLimitBl = serviceProvider.GetRequiredService<IParserDateLimitBl>();

        var batchData = new List<InTopicModelDto<QuestionRecord>>();

        var existingData = dataStore.GetNewestQuestionRecord();

        int iterationCount = 0;
        DateOnly startDate = DateOnly.FromDateTime(DateTime.Today);
        var stopDate = dateLimitBl.GetStopDateAsync(ParserType.QuestionsParserApp.ToString(), cancellationToken).GetAwaiter().GetResult() ?? new DateOnly(2022, 4, 28);
        TimeOnly stopTime = new TimeOnly(0,0);
        if (existingData != null)
        {
            stopDate = existingData.Date;
            stopTime = existingData.Time;
        }
        DateOnly currentDate = startDate;

        parser.Navigate(config.ParserForOzonSellerSiteUrl);

        while (currentDate >= stopDate && !cancellationToken.IsCancellationRequested)
        {
            string dateStrNow = currentDate.ToString("dd.MM.yyyy");
            string dateStrLast = currentDate.ToString("dd.MM.yyyy");
            string rangeStr = $"{dateStrLast} – {dateStrNow}";

            bool dateParsedOk = false;
            int attempts = 0;
            while (!dateParsedOk && attempts < 3)
            {
                attempts++;
                try
                {
                    parser.SetDateRange(rangeStr);
                    var rows = parser.WaitForStableTableRows().GetAwaiter().GetResult();
                    if (rows == null || rows.Count == 0)
                    {
                        Console.WriteLine("Строки таблицы не найдены");
                    }
                    else
                    {
                        var data = parser.ExtractTableData(stopDate, stopTime);
                        batchData.AddRange(data);
                    }
                    dateParsedOk = true;
                }
                catch
                {
                    if (attempts < 3)
                    {
                        currentDate = currentDate.AddDays(-1);
                        parser.Navigate(config.ParserForOzonSellerSiteUrl);
                        break;
                    }
                    else
                    {
                    }
                }
            }

            iterationCount++;
            if (iterationCount % 1 == 0)
            {
                if (batchData.Count >= 1)
                {
                    dataStore.SaveOrUpdateQuestions(batchData.Select(x => x.Model), ct: cancellationToken);

                    foreach (var entity in batchData.Where(x => x.InTopic == true).Select(x => x.Model))
                    {
                            var uniqueKey = $"q_{entity.Date:dd.MM.yyyy}_{entity.Time:HH:mm}_{entity.ClientName}";
                            var existingEntry = newDataRepository.GetEntryBySourceRecordId(uniqueKey);
                            if (existingEntry == null)
                            {
                                var newEntry = new NewDataEntry
                                {
                                    ParserName = "QuestionsParserApp",
                                    SourceRecordId = uniqueKey,
                                    CreatedAt = DateTime.UtcNow,
                                    Processed = false
                                };
                                newDataRepository.AddEntry(newEntry);
                        }
                    }

                    batchData.Clear();
                }
            }

            currentDate = currentDate.AddDays(-1);
        }

        if (batchData.Count >= 1)
        {
            dataStore.SaveOrUpdateQuestions(batchData.Select(x => x.Model), cancellationToken);
        }

        Console.WriteLine("Парсинг завершен.");
    }
}
