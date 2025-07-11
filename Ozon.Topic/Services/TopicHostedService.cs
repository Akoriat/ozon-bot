using Bl.Implementations.Abstract;
using Bl.Interfaces;
using Common.Configuration.Configs;
using Common.Enums;
using DAL.Models;
using Entities.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ozon.Topic.Services;

public class TopicHostedService : ResilientBackgroundService
{
    private readonly IQuestionDataStoreBl _questionDataStoreBl;
    private readonly IForumTopicService _forumTopicService;
    private readonly IReviewDataStoreBl _reviewDataStoreBl;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TopicHostedService> _logger;
    private readonly PromtsConfig _promptsConfig;
    private readonly IParsersModeBl _parsersModeBl;
    private readonly ChatGptConfig _chatGptConfig;

    public TopicHostedService(IServiceScopeFactory scopeFactory
        , IForumTopicService forumTopicService
        , IQuestionDataStoreBl questionDataStoreBl
        , IReviewDataStoreBl reviewDataStoreBl
        , ILogger<TopicHostedService> logger
        , IOptions<PromtsConfig> promptsOptions
        , IOptions<ChatGptConfig> chatGptOptions
        , IParsersModeBl parsersModeBl)
    {
        _forumTopicService = forumTopicService;
        _questionDataStoreBl = questionDataStoreBl;
        _reviewDataStoreBl = reviewDataStoreBl;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _promptsConfig = promptsOptions.Value;
        _chatGptConfig = chatGptOptions.Value;
        _parsersModeBl = parsersModeBl;
    }

    protected override async Task RunServiceAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TopicHostedService started {DateTime}", DateTime.UtcNow);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var dataRepositoryBl = scope.ServiceProvider.GetRequiredService<INewDataRepositoryBl>();
                    var chatParserBl = scope.ServiceProvider.GetRequiredService<IChatParserBl>();

                    var unprocessed = await dataRepositoryBl.GetUnprocessedEntriesAsync();
                    var modes = (await _parsersModeBl.GetAllModesAsync(stoppingToken)).Where(x => x.IsActive == true).Select(x => x.ParserName);

                    foreach (var entry in unprocessed.Where(x => modes.Contains(x.ParserName)))
                    {
                        try
                        {
                            await ProcessEntryAsync(entry, chatParserBl, dataRepositoryBl, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Ошибка при обработке записи {@Entry}. Идентификатор: {EntryId}",
                                entry, entry.Id);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Критическая ошибка основного цикла TopicHostedService");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
        _logger.LogInformation("TopicHostedService stopped");
    }


    private async Task ProcessEntryAsync(
        NewDataEntry entry,
        IChatParserBl chatParserBl,
        INewDataRepositoryBl dataRepositoryBl,
        CancellationToken ct)
    {
        switch (entry.ParserName)
        {
            case "ChatParserApp":
                await HandleChatAsync(entry, chatParserBl, dataRepositoryBl, ct);
                break;

            case "QuestionsParserApp":
                await HandleQuestionAsync(entry, dataRepositoryBl, ct);
                break;

            case "ReviewsParser":
                await HandleReviewAsync(entry, dataRepositoryBl, ct);
                break;

            default:
                _logger.LogWarning("Неизвестный ParserName: {Parser}", entry.ParserName);
                break;
        }
    }

    private async Task HandleReviewAsync(NewDataEntry entry, INewDataRepositoryBl dataRepositoryBl, CancellationToken ct)
    {
        var reviewEntity = await _reviewDataStoreBl.GetReviewByUniqueKey(entry.SourceRecordId);

        string? history;
        if (!string.IsNullOrEmpty(reviewEntity.Dialog))
            history = _promptsConfig.ReviewIfHaveDialog
                .Replace("TOVAR", reviewEntity.Product)
                .Replace("CLIENTNAME", reviewEntity.Name)
                .Replace("RATING", reviewEntity.Rating.ToString())
                .Replace("PHOTO", reviewEntity.Photo != "—" ? "Да" : "Нет")
                .Replace("VIDEO", reviewEntity.Video != "—" ? "Да" : "Нет")
                .Replace("ARTICLE", reviewEntity.Article)
                .Replace("COMMENT", reviewEntity.ReviewText)
                .Replace("DIALOG", reviewEntity.Dialog);
        else
        {
            history = _promptsConfig.ReviewIfNotHaveDialog
                .Replace("TOVAR", reviewEntity.Product)
                .Replace("CLIENTNAME", reviewEntity.Name)
                .Replace("RATING", reviewEntity.Rating.ToString())
                .Replace("PHOTO", reviewEntity.Photo != "—" ? "Да" : "Нет")
                .Replace("VIDEO", reviewEntity.Video != "—" ? "Да" : "Нет")
                .Replace("ARTICLE", reviewEntity.Article)
                .Replace("COMMENT", reviewEntity.ReviewText);
        }

        var reviewText = reviewEntity.ReviewText;

        var assType = reviewEntity.Rating > 3 ?
            AssistantType.ReviewGoodId :
            AssistantType.ReviewBadId;

        var green = char.ConvertFromUtf32(0x1F7E2);
        var red = char.ConvertFromUtf32(0x1F534);

        string topicName =
            reviewEntity.Rating > 3
            ? $"{green}ОП{reviewEntity.Rating}_{reviewEntity.ReviewDate},{reviewEntity.ReviewTime}"
            : $"{red}ОО{reviewEntity.Rating}_{reviewEntity.ReviewDate},{reviewEntity.ReviewTime}";

        await _forumTopicService.CreateTopicForRequestAsync(new CreateTopicDto
        {
            RequestId = entry.SourceRecordId,
            ForChatGpt = history,
            UserQuestion = reviewText,
            ParserName = entry.ParserName,
            ClientName = reviewEntity.Name,
            Product = reviewEntity.Product,
            AssistantType = assType,
            Rating = reviewEntity.Rating,
            Article = reviewEntity.Article,
            Photo = reviewEntity.Photo,
            Video = reviewEntity.Video,
            TopicName = topicName,
            FullChat = reviewEntity.Dialog
        }
        , ct
        );

        await dataRepositoryBl.MarkEntryAsProcessedAsync(entry.Id);
    }

    private async Task HandleQuestionAsync(NewDataEntry entry, INewDataRepositoryBl dataRepositoryBl, CancellationToken ct)
    {
        var questionEntity = await _questionDataStoreBl.GetQuestionByKey(entry.SourceRecordId);

        var history = "";
        var message = questionEntity.Question;
        if (!string.IsNullOrEmpty(questionEntity.ChatConversation))
        {
            message = questionEntity.ChatConversation.Split("Сообщение#:").LastOrDefault() ?? questionEntity.Question;
            history = _promptsConfig.QuestionIfHaveDialog
                .Replace("TOVAR", questionEntity.Product)
                .Replace("CLIENTNAME", questionEntity.ClientName)
                .Replace("ARTICLE", questionEntity.Article)
                .Replace("COMMENT", message)
                .Replace("DIALOG", questionEntity.ChatConversation);
        }
        else
        {
            history = _promptsConfig.QuestionIfNotHaveDialog
                .Replace("TOVAR", questionEntity.Product)
                .Replace("CLIENTNAME", questionEntity.ClientName)
                .Replace("ARTICLE", questionEntity.Article)
                .Replace("COMMENT", message);
        }

        if (string.IsNullOrEmpty(message))
        {
            message = questionEntity.Question;
        }

        var articlePartial = string.Join("", questionEntity.Article.Take(2).ToList());
        var assType = articlePartial switch
        {
            "BR" => AssistantType.QuestionsBrId,
            "CH" => AssistantType.QuestionsChId,
            "KR" => AssistantType.QuestionsKrId,
            "DS" or "DN" => AssistantType.QuestionsDsOrDnId,
            _ => AssistantType.QuestionsOthersId,
        };

        var yellow = char.ConvertFromUtf32(0x1F7E1);

        string topicName = assType switch
        {
            AssistantType.QuestionsBrId => $"{yellow}ВБ_{questionEntity.Date},{questionEntity.Time}",
            AssistantType.QuestionsChId => $"{yellow}ВЧ_{questionEntity.Date},{questionEntity.Time}",
            AssistantType.QuestionsKrId => $"{yellow}ВК_{questionEntity.Date},{questionEntity.Time}",
            AssistantType.QuestionsDsOrDnId => $"{yellow}ВД_{questionEntity.Date},{questionEntity.Time}",
            AssistantType.QuestionsOthersId => $"{yellow}ВП_{questionEntity.Date},{questionEntity.Time}",
            _ => throw new NotImplementedException(),
        };

        await _forumTopicService.CreateTopicForRequestAsync(new CreateTopicDto
        {
            RequestId = entry.SourceRecordId,
            ForChatGpt = history,
            UserQuestion = message,
            ParserName = entry.ParserName,
            ClientName = questionEntity.ClientName,
            Product = questionEntity.Product,
            AssistantType = assType,
            Article = questionEntity.Article,
            Photo = "---",
            Video = "---",
            TopicName = topicName,
            FullChat = questionEntity.ChatConversation,
        }
        , ct
        );

        await dataRepositoryBl.MarkEntryAsProcessedAsync(entry.Id);
    }
    static string GetMessage(string src) =>
Regex.Replace(src, @"^\s*•?\s*\d{1,2}:\d{2}\s*", "").Trim();
    private async Task HandleChatAsync(NewDataEntry entry, IChatParserBl chatParserBl, INewDataRepositoryBl dataRepositoryBl, CancellationToken ct)
    {
        var chatEntity = chatParserBl.GetChatRecordByChatId(entry.SourceRecordId);
        var cleanHistory = Clean(chatEntity.History);

        var listOfTokens = cleanHistory.Split("\r\n").Reverse().ToList();
        var message = GetMessage(listOfTokens.First());
        var proverka = TimeOnly.TryParseExact(message, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time);

        if (proverka)
            return;

        var history = _promptsConfig.Chat
                .Replace("CLIENTNAME", chatEntity.Title)
                .Replace("COMMENT", message)
                .Replace("DIALOG", cleanHistory);

        var blue = char.ConvertFromUtf32(0x1F535);

        string TopicName = $"{blue}Ч_{chatEntity.Title}_{chatEntity.Date}";

        await _forumTopicService.CreateTopicForRequestAsync(new CreateTopicDto
        {
            RequestId = entry.SourceRecordId,
            ForChatGpt = history,
            UserQuestion = message,
            ParserName = entry.ParserName,
            ClientName = chatEntity.Title,
            Product = "",
            AssistantType = AssistantType.ChatGeneralId,
            TopicName = TopicName,
            FullChat = cleanHistory
        }
        , ct
        );

        await dataRepositoryBl.MarkEntryAsProcessedAsync(entry.Id);
    }

    public static string Clean(string rawChat)
    {
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "онлайн","покупатель","быстрые ответы",
    "вы пишете покупателю","новые сообщения"
};

        var ruMonths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
{
    {"января",1},{"февраля",2},{"марта",3},{"апреля",4},
    {"мая",5},{"июня",6},{"июля",7},{"августа",8},
    {"сентября",9},{"октября",10},{"ноября",11},{"декабря",12}
};

        var dateRx = new Regex(@"^(\d{1,2})\s+([А-Яа-я]+)$");
        var timeRx = new Regex(@"^\d{1,2}:\d{2}$");

        var result = new Dictionary<DateTime, List<string>>();
        DateTime? currentDate = null;
        string? pendingText = null;

        using var sr = new StringReader(rawChat);
        string? line;
        while ((line = sr.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0) continue;
            if (skip.Contains(line)) continue;

            // 1. Дата «16 сентября»
            var dm = dateRx.Match(line);
            if (dm.Success && ruMonths.TryGetValue(dm.Groups[2].Value, out int m))
            {
                int d = int.Parse(dm.Groups[1].Value);
                currentDate = new DateTime(DateTime.Now.Year, m, d);
                continue;
            }

            // 2. Время «00:24»
            if (timeRx.IsMatch(line))
            {
                if (currentDate == null) continue;          // безопасность

                if (!result.TryGetValue(currentDate.Value, out var list))
                    list = result[currentDate.Value] = new List<string>();

                // если текста нет → значит был файл
                string msg = pendingText ?? "[вложение]";
                list.Add($"{line}  {msg}");

                pendingText = null;                         // сброс
                continue;
            }

            // 3. Обычный текст сообщения
            pendingText = line;
        }

        // 4. Финальный вывод
        var sb = new StringBuilder();
        foreach (var kv in result.OrderBy(k => k.Key))
        {
            sb.AppendLine(kv.Key.ToString("d MMMM yyyy", new CultureInfo("ru-RU")));
            foreach (var msg in kv.Value)
                sb.AppendLine("  • " + msg);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }


}
