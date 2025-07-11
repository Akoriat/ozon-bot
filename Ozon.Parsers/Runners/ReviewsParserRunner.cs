using Bl.Interfaces;
using Common.Configuration.Configs;
using Common.Enums;
using DAL.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Ozon.Parsers.Runners
{
    public static class ReviewsParserRunner
    {
        public static void RunReviewsParserApp(IServiceProvider sp, ParserConfig config, CancellationToken ct)
        {
            var newDataRepository = sp.GetRequiredService<INewDataRepositoryBl>();
            var parser = sp.GetRequiredService<IOzonReviewParserService>();
            var reviewBl = sp.GetRequiredService<IReviewDataStoreBl>();
            var dateLimitBl = sp.GetRequiredService<IParserDateLimitBl>();

            string reviewsUrl = config.ReviewsParserAppSiteUrl;
            parser.Navigate(reviewsUrl);

            DateOnly lastReviewDate = DateOnly.MinValue;
            TimeOnly lastReviewTime = TimeOnly.MinValue;

            var reviews = reviewBl.GetNewestReview();
            if (reviews != null)
            {
                lastReviewDate = reviews.ReviewDate;
                lastReviewTime = reviews.ReviewTime;
            }

            int iterationCount = 0;
            bool hasMore = true;

            var dateNow = DateOnly.FromDateTime(DateTime.Now);
            var stopDate = dateLimitBl.GetStopDateAsync(ParserType.ReviewsParser.ToString(), ct).GetAwaiter().GetResult() ?? DateOnly.MinValue;

            while (hasMore && !ct.IsCancellationRequested && dateNow >= stopDate)
            {
                iterationCount++;

                for (int i = 0; i < 3; i++)
                {
                    if (parser.ClickCalendarDate(dateNow).GetAwaiter().GetResult())
                    {
                        break;
                    }
                    parser.Navigate(reviewsUrl);
                }
                try
                {
                    (var reviewsResultFromIteration, hasMore) = parser.ParseIteration(lastReviewDate, lastReviewTime, ct).GetAwaiter().GetResult();

                    reviewBl.AddReviews(reviewsResultFromIteration.Select(x => x.Model).ToList());

                    var inTopicResult = reviewsResultFromIteration.Where(x => x.InTopic == true).Select(x => x.Model);
                    foreach (var review in inTopicResult)
                    {
                        if (string.IsNullOrEmpty(review.ReviewText.Trim()))
                            continue;
                        string uniqueKey = $"r_{review.ReviewDate:dd.MM.yyyy}_{review.ReviewTime:HH:mm}_{review.Name.Trim()}";
                        var existingEntry = newDataRepository.GetEntryBySourceRecordId(uniqueKey);
                        if (existingEntry == null)
                        {
                            var newEntry = new NewDataEntry
                            {
                                ParserName = "ReviewsParser",
                                SourceRecordId = uniqueKey,
                                CreatedAt = DateTime.UtcNow,
                                Processed = false
                            };
                            newDataRepository.AddEntry(newEntry);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Ошибка в итерации: " + ex.Message);
                }

                Thread.Sleep(1000);
                if (ct.IsCancellationRequested || !hasMore) break;

                dateNow = dateNow.AddDays(-1);
            }

            Console.WriteLine("Парсинг завершён.");
        }
    }
}
