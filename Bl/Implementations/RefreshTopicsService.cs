using Bl.Common.Configs;
using Bl.Common.DTOs;
using Bl.Common.Enum;
using Bl.Interfaces;
using DAL.Models;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TL;

namespace Bl.Implementations
{
    public class RefreshTopicsService : IRefreshTopicsService
    {
        private readonly IParserForOzonSellerService _parserForOzonSellerService;
        private readonly IOzonReviewParserService _ozonReviewParserService;
        private readonly IOptions<ParserConfig> _options;
        private readonly INewDataRepositoryBl _newDataRepositoryBl;
        private readonly IQuestionDataStoreBl _questionDataStoreBl;
        private readonly IReviewDataStoreBl _reviewDataStoreBl;
        private readonly IParsersModeBl _parsersModeBl;
        public RefreshTopicsService(IParserForOzonSellerService parserForOzonSellerService
            , IOzonReviewParserService ozonReviewParserService
            , IOptions<ParserConfig> options
            , INewDataRepositoryBl newDataRepositoryBl
            , IQuestionDataStoreBl questionDataStoreBl,
IReviewDataStoreBl reviewDataStoreBl,
IParsersModeBl parsersModeBl)
        {
            _ozonReviewParserService = ozonReviewParserService;
            _parserForOzonSellerService = parserForOzonSellerService;
            _options = options;
            _newDataRepositoryBl = newDataRepositoryBl;
            _questionDataStoreBl = questionDataStoreBl;
            _reviewDataStoreBl = reviewDataStoreBl;
            _parsersModeBl = parsersModeBl;
        }
        public void RefreshTopics(RefreshTopicDto refreshTopicDto, CancellationToken cancellationToken)
        {
            Console.WriteLine("ПРОСМОТР ПОСЛЕДНИХ ОБРАБОТАННЫХ СУЩНОСТЕЙ");
            var modes = _parsersModeBl.GetAllModesAsync(cancellationToken).Result.Where(x => x.IsActive == true).Select(x => x.ParserName);

            if (modes.Contains(ParserType.QuestionsParserApp.ToString()))
            {
                RefreshTopicsForQuestions(refreshTopicDto.RequestIdsForQuestion, cancellationToken);
                Console.WriteLine("ПРОСМОТР ПОСЛЕДНИХ ОБРАБОТАННЫХ СУЩНОСТЕЙ ДЛЯ ВОПРОСОВ ЗАВЕРШЕН");
            }
            if (modes.Contains(ParserType.ReviewsParser.ToString()))
            {
                RefreshTopicsForReviews(refreshTopicDto.RequestIdsForReviews, cancellationToken);
                Console.WriteLine("ПРОСМОТР ПОСЛЕДНИХ ОБРАБОТАННЫХ СУЩНОСТЕЙ ДЛЯ ОТЗЫВОВ ЗАВЕРШЕН");
            }

        }

        private void RefreshTopicsForQuestions(IEnumerable<string> requestIds, CancellationToken cancellationToken)
        {
            RefreshTopicsForQuestions(requestIds, _options.Value.ParserForOzonSellerSiteUrl, cancellationToken);
        }
        private void RefreshTopicsForReviews(IEnumerable<string> requestIds, CancellationToken cancellationToken)
        {
            RefreshTopicsForReviews(requestIds, _options.Value.ReviewsParserAppSiteUrl, cancellationToken);
        }

        private void RefreshTopicsForQuestions(IEnumerable<string> requestIds, string url, CancellationToken cancellationToken)
        {
            int viewedCounter = 0;

            var batchData = new List<InTopicModelDto<QuestionRecord>>();

            int iterationCount = 0;
            DateOnly startDate = DateOnly.FromDateTime(DateTime.Today);
            DateOnly stopDate = new DateOnly(2022, 4, 28);
            TimeOnly stopTime = new TimeOnly(0, 0);

            DateOnly currentDate = startDate;

            _parserForOzonSellerService.Navigate(url);

            _parserForOzonSellerService.ProcessedButtonClick();

            Thread.Sleep(500);

            while (currentDate >= stopDate && viewedCounter < 50 && !cancellationToken.IsCancellationRequested)
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
                        _parserForOzonSellerService.SetDateRange(rangeStr);
                        var rows = _parserForOzonSellerService.WaitForStableTableRows().GetAwaiter().GetResult();
                        if (rows == null || rows.Count == 0)
                        {
                            Console.WriteLine("Строки таблицы не найдены");
                        }
                        else
                        {
                            var data = _parserForOzonSellerService.ExtractTableData(stopDate, stopTime);
                            batchData.AddRange(data);
                        }
                        dateParsedOk = true;
                    }
                    catch (Exception ex)
                    {
                        if (attempts < 3)
                        {
                            currentDate = currentDate.AddDays(-1);
                            _parserForOzonSellerService.Navigate(url);
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
                        _questionDataStoreBl.SaveOrUpdateQuestions(batchData.Select(x => x.Model), cancellationToken);

                        foreach (var entity in batchData.Where(x => x.InTopic == true).Select(x => x.Model))
                        {
                            var uniqueKey = $"q_{entity.Date:dd.MM.yyyy}_{entity.Time:HH:mm}_{entity.ClientName}";
                            if (requestIds.Contains(uniqueKey))
                                continue;

                            var newEntry = new NewDataEntry
                            {
                                ParserName = "QuestionsParserApp",
                                SourceRecordId = uniqueKey,
                                CreatedAt = DateTime.UtcNow,
                                Processed = false
                            };
                            _newDataRepositoryBl.AddEntry(newEntry);
                        }
                    }
                    viewedCounter += batchData.Count;
                    batchData.Clear();
                }

                currentDate = currentDate.AddDays(-1);

                if (batchData.Count >= 1)
                {
                    _questionDataStoreBl.SaveOrUpdateQuestions(batchData.Select(x => x.Model), cancellationToken);
                }
            }

            Console.WriteLine("RefreshTopicsForQuestions завершен.");
        }
        private void RefreshTopicsForReviews(IEnumerable<string> requestIds, string url, CancellationToken cancellationToken)
        {
            string reviewsUrl = url;
            _ozonReviewParserService.Navigate(reviewsUrl);

            _ozonReviewParserService.ProcessedButtonClick();

            DateOnly lastReviewDate = DateOnly.MinValue;
            TimeOnly lastReviewTime = TimeOnly.MinValue;

            int iterationCount = 0;
            bool hasMore = true;
            var dateNow = DateOnly.FromDateTime(DateTime.Now);
            int viewedCounter = 0;

            while (hasMore && viewedCounter < 50 && !cancellationToken.IsCancellationRequested)
            {
                iterationCount++;
                for (int i = 0; i < 3; i++)
                {
                    if (_ozonReviewParserService.ClickCalendarDate(dateNow).GetAwaiter().GetResult())
                    {
                        break;
                    }
                    _ozonReviewParserService.Navigate(reviewsUrl);
                }
                try
                {
                    (var reviewsResultFromIteration, hasMore) = _ozonReviewParserService.ParseIteration(lastReviewDate, lastReviewTime, cancellationToken).GetAwaiter().GetResult();

                    _reviewDataStoreBl.AddReviews(reviewsResultFromIteration.Select(x => x.Model).ToList());
                    Console.WriteLine($"Сохранено {reviewsResultFromIteration.Count} записей");
                    viewedCounter += reviewsResultFromIteration.Count;

                    var inTopicResult = reviewsResultFromIteration.Where(x => x.InTopic == true).Select(x => x.Model);
                    foreach (var review in inTopicResult)
                    {
                        if (string.IsNullOrEmpty(review.ReviewText.Trim()))
                            continue;

                        string uniqueKey = $"r_{review.ReviewDate:dd.MM.yyyy}_{review.ReviewTime:HH:mm}_{review.Name.Trim()}";
                        if (requestIds.Contains(uniqueKey))
                            continue;

                        var newEntry = new NewDataEntry
                        {
                            ParserName = "ReviewsParser",
                            SourceRecordId = uniqueKey,
                            CreatedAt = DateTime.UtcNow,
                            Processed = false
                        };
                        _newDataRepositoryBl.AddEntry(newEntry);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Ошибка в итерации: " + ex.Message);
                }

                Thread.Sleep(1000);
                if (cancellationToken.IsCancellationRequested || !hasMore || viewedCounter > 50) break;

                dateNow = dateNow.AddDays(-1);
            }

            Console.WriteLine("RefreshTopicsForReviews завершён.");
        }
    }
}
