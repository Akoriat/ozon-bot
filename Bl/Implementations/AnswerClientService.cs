using Bl.Interfaces;
using Common.Configuration.Configs;
using DAL.Migrations;
using DAL.Models;
using DocumentFormat.OpenXml.Spreadsheet;
using Entities.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ActiveTopic = DAL.Models.ActiveTopic;

namespace Bl.Implementations;

public class AnswerClientService : IAnswerClientService
{
    private readonly IChatParserService _chatParserService;
    private readonly IQuestionParserService _parserForOzonSellerService;
    private readonly IOzonReviewParserService _parserForOzonReviewParserService;
    private readonly ParserConfig _parserConfig;
    private readonly IReviewDataStoreBl _reviewDataStoreBl;
    private readonly ILogger<AnswerClientService> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly HashSet<string> _inProgressRequests = [];
    public AnswerClientService(IChatParserService chatParserService
        , IQuestionParserService parserForOzonSellerService
        , IOzonReviewParserService parserForOzonReviewParserService
        , IOptions<ParserConfig> parserConfig
        , ILogger<AnswerClientService> logger
        , IReviewDataStoreBl reviewDataStoreBl)
    {
        _chatParserService = chatParserService;
        _parserForOzonReviewParserService = parserForOzonReviewParserService;
        _parserForOzonSellerService = parserForOzonSellerService;
        _parserConfig = parserConfig.Value;
        _logger = logger;
        _reviewDataStoreBl = reviewDataStoreBl;
    }

    public bool RefreshTopic(ActiveTopic activeTopic)
    {
        var key = activeTopic.RequestId;

        if (!_inProgressRequests.Add(key))
        {
            _logger.LogWarning("Повторный запрос для RequestId {RequestId}, игнорируем", key);
            return false;
        }

        _semaphore.Wait();
        try
        {
            var parserName = activeTopic.ParserName;

            if (parserName == "ChatParserApp")
            {
                _chatParserService.Navigate(_parserConfig.ChatParserSiteUrl);
                return _chatParserService.RefreshActiveTopic(activeTopic.RequestId);
            }
            else if (parserName == "QuestionsParserApp")
            {
                _parserForOzonSellerService.Navigate(_parserConfig.ParserForOzonSellerSiteUrl);
                return _parserForOzonSellerService.RefreshActiveTopic(activeTopic.RequestId);
            }
            else //ReviewsParser
            {
                _parserForOzonReviewParserService.Navigate(_parserConfig.ReviewsParserAppSiteUrl);
                return _parserForOzonReviewParserService.RefreshActiveTopic(activeTopic.RequestId, activeTopic.Article!);
            }
        }
        finally
        {
            _inProgressRequests.Remove(key);
            _semaphore.Release();
        }
    }

    public bool SendMessageToClientAutoAssistant(SendMessageToClientAutoAssistantDto topic)
    {
        var key = topic.RequestId;

        if (!_inProgressRequests.Add(key))
        {
            _logger.LogWarning("Повторный запрос для RequestId {RequestId}, игнорируем", key);
            return false;
        }

        _semaphore.Wait();
        try
        {
            var parserName = topic.ParserName;
            var message = topic.GptDraftAnswer;
            if (parserName == "ChatParserApp")
            {
                _chatParserService.Navigate(_parserConfig.ChatParserSiteUrl);
                return _chatParserService.SendMessageToClient(topic.RequestId, message);
            }
            else if (parserName == "QuestionsParserApp")
            {
                _parserForOzonSellerService.Navigate(_parserConfig.ParserForOzonSellerSiteUrl);
                return _parserForOzonSellerService.SendMessageToClient(topic.RequestId, message);
            }
            else //ReviewsParser
            {
                var review = _reviewDataStoreBl.GetReviewByUniqueKey(topic.RequestId).Result;
                _parserForOzonReviewParserService.Navigate(_parserConfig.ReviewsParserAppSiteUrl);
                bool result;
                do
                {
                    result = _parserForOzonReviewParserService.SendMessageToClient(topic.RequestId, message, review.Article!);
                } while (!result);
                return result;
            }
        }
        finally
        {
            _inProgressRequests.Remove(key);
            _semaphore.Release();
        }
    }

    public bool SendMessageToClientManualAssistant(TopicRequest topic, string? adminMessage = null)
    {
        var key = topic.RequestId;

        if (!_inProgressRequests.Add(key))
        {
            _logger.LogWarning("Повторный запрос для RequestId {RequestId}, игнорируем", key);
            return false;
        }

        _semaphore.Wait();
        try
        {
            var parserName = topic.ParserName;
            var message = adminMessage ?? topic.GptDraftAnswer;
            if (parserName == "ChatParserApp")
            {
                _chatParserService.Navigate(_parserConfig.ChatParserSiteUrl);
                return _chatParserService.SendMessageToClient(topic.RequestId, message);
            }
            else if (parserName == "QuestionsParserApp")
            {
                _parserForOzonSellerService.Navigate(_parserConfig.ParserForOzonSellerSiteUrl);
                return _parserForOzonSellerService.SendMessageToClient(topic.RequestId, message);
            }
            else //ReviewsParser
            {
                var review = _reviewDataStoreBl.GetReviewByUniqueKey(topic.RequestId).Result;
                _parserForOzonReviewParserService.Navigate(_parserConfig.ReviewsParserAppSiteUrl);
                var count = 0;
                bool result;
                do
                {
                    result = _parserForOzonReviewParserService.SendMessageToClient(topic.RequestId, message, review.Article!);
                    count++;
                } while (!result && count < 3);
                return result;
            }
        }
        finally
        {
            _inProgressRequests.Remove(key);
            _semaphore.Release();
        }
    }
}
