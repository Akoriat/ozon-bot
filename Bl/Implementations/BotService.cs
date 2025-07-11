using Bl.Extensions;
using Bl.Interfaces;
using Common.Configuration.Configs;
using Common.Enums;
using DAL.Models;
using Entities.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Update = Telegram.Bot.Types.Update;

namespace Bl.Implementations;

public class BotService : IBotService
{
    private readonly ITelegramBotClient _botClient;
    private readonly IAnswerClientService _answerClientService;
    private readonly ITopicRequestBl _topicRequestBl;
    private readonly IChatGPTClient _chatGPTClient;
    private readonly ICorrectAnswerBl _correctAnswerBl;
    private readonly IAssistantModeBl _assistantModeBl;
    private readonly BotConfig _botConfig;
    private readonly ILogger<BotService> _logger;
    private readonly IActiveTopicBl _activeTopicBl;
    private readonly IRefreshTopicsService _refreshTopicsService;
    private readonly INewDataRepositoryBl _dataRepositoryBl;
    private readonly IChatParserBl _chatParserBl;
    private readonly IParsersModeBl _parsersModeBl;
    private readonly IParserDateLimitBl _parserDateLimitBl;
    private readonly ILastMessageIdFromGeneralBl _lastMessageBL;

    private readonly Channel<CallbackQuery> _callbackQueue;
    private readonly Dictionary<string, string> _URLis;
    private readonly string? _answerPrompt;
    private static readonly SemaphoreSlim _refreshLock = new(1, 1);
    private enum AwaitMode { None, Answer, Correcting }

    private record AwaitState(AwaitMode Mode, DateTime SetAt);

    private readonly ConcurrentDictionary<int, AwaitState> _awaiting = new();
    private readonly ConcurrentDictionary<long, string> _awaitingDate = new();

    private const int AwaitTimeoutMinutes = 10;
    private const string DateOnlyFormat = "dd.MM.yyyy";

    public BotService(
        ITelegramBotClient botClient,
        IOptions<BotConfig> botConfig,
        IChatGPTClient chatGPTClient,
        IAnswerClientService answerClientService,
        ITopicRequestBl topicRequestBl,
        IAssistantModeBl assistantModeBl,
        ILogger<BotService> logger,
        ICorrectAnswerBl correctAnswerBl,
        IActiveTopicBl activeTopicBl,
        IRefreshTopicsService refreshTopicsService,
        INewDataRepositoryBl newDataRepositoryBl,
        IChatParserBl chatParserBl,
        IOptions<UrlsConfig> URLsOptions,
        IOptions<PromtsConfig> promptsOptions,
        IParsersModeBl parsersModeBl,
        IParserDateLimitBl parserDateLimitBl,
        Channel<CallbackQuery> callbackQueue,
        ILastMessageIdFromGeneralBl lastMessageBl)
    {
        _botClient = botClient;
        _botConfig = botConfig.Value;
        _URLis = new Dictionary<string, string>
            {
                { nameof(URLsOptions.Value.ReviewGood), URLsOptions.Value.ReviewGood },
                { nameof(URLsOptions.Value.ReviewBad), URLsOptions.Value.ReviewBad },
                { nameof(URLsOptions.Value.ChatGeneral), URLsOptions.Value.ChatGeneral },
                { nameof(URLsOptions.Value.QuestionsOthers), URLsOptions.Value.QuestionsOthers },
                { nameof(URLsOptions.Value.QuestionsBr), URLsOptions.Value.QuestionsBr },
                { nameof(URLsOptions.Value.QuestionsCh), URLsOptions.Value.QuestionsCh },
                { nameof(URLsOptions.Value.QuestionsKr), URLsOptions.Value.QuestionsKr },
                { nameof(URLsOptions.Value.QuestionsDsOrDn), URLsOptions.Value.QuestionsDsOrDn }
            };
        _answerClientService = answerClientService;
        _topicRequestBl = topicRequestBl;
        _chatGPTClient = chatGPTClient;
        _logger = logger;
        _correctAnswerBl = correctAnswerBl;
        _assistantModeBl = assistantModeBl;
        _activeTopicBl = activeTopicBl;
        _refreshTopicsService = refreshTopicsService;
        _dataRepositoryBl = newDataRepositoryBl;
        _chatParserBl = chatParserBl;
        _answerPrompt = promptsOptions.Value.AnswerPrompt;
        _parsersModeBl = parsersModeBl;
        _parserDateLimitBl = parserDateLimitBl;
        _callbackQueue = callbackQueue;
        _lastMessageBL = lastMessageBl;
    }

    public async Task HandleUpdateAsync(Update update, CancellationToken ct)
    {
        try
        {
            switch (update.Type)
            {
                case UpdateType.CallbackQuery:
                    await HandleCallbackQueryAsync(update.CallbackQuery!, ct);
                    break;
                case UpdateType.Message:
                    await HandleMessageAsync(update.Message!.ToDto(), ct);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update handling failed: {UpdateId}", update.Id);
        }
    }
    private async Task HandleMessageAsync(TelegramMessageDto msg, CancellationToken ct)
    {
        if (msg.ChatId == _botConfig.ForumChatId && (msg.ThreadId ?? 0) == 0)
        {
            await _lastMessageBL.AddOrUpdateAsync(new LastMessageDto
            {
                LastMessageId = msg!.MessageId,
                LastMessageType = LastMessageType.Message
            });
        }

        foreach (var (thread, state) in _awaiting)
            if (state.SetAt < DateTime.UtcNow.AddMinutes(-AwaitTimeoutMinutes))
                _awaiting.TryRemove(thread, out _);

        if (_awaitingDate.TryGetValue(msg.UserId, out var parserName) && !msg.Text!.StartsWith('/'))
        {
            if (DateOnly.TryParseExact(msg.Text, DateOnlyFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                await _parserDateLimitBl.SetStopDateAsync(parserName, dt, ct);
                await _botClient.SendMessage(msg.ChatId, $"Дата для {parserName} установлена на {dt:dd.MM.yyyy}", cancellationToken: ct);
                _awaitingDate.TryRemove(msg.UserId, out _);
            }
            else
            {
                await _botClient.SendMessage(msg.ChatId, "Неверный формат. Введите дату в формате dd.MM.yyyy", replyParameters: msg.MessageId, cancellationToken: ct);
            }
            return;
        }

        if (msg?.Text == "/command")
        {
            var commands = new[]
            {
                "/parsers",
                "/refresh",
                "/status",
                "/urls",
                "/dates",
                "/setdate",
                "/answer (/a)",
                "/correcting (/c)",
                "/delete"
            };
            var text = "Доступные команды:\n" + string.Join("\n", commands);
            await _botClient.SendMessage(msg.ChatId, text, cancellationToken: ct);
            return;
        }

        if (msg!.ThreadId is int tid &&
            _awaiting.TryRemove(tid, out var st) &&
            !msg.Text!.StartsWith('/'))
        {
            if (st.Mode == AwaitMode.Answer)
                msg.Text = "/answer " + msg.Text;
            else
                msg.Text = "/correcting " + msg.Text;

            if (st.Mode == AwaitMode.Answer)
                await OnManualAnswerAsync(msg, ct);
            else
                await OnCorrectingManualAnswer(msg, ct);

            return;
        }
        if (msg?.Text == "/parsers")
        {
            var keyboard = await BuildParsersKeyboardAsync(ct);
            await _botClient.SendMessage(
                chatId: msg.ChatId,
                text: "Статусы парсеров (нажмите, чтобы переключить):",
                replyMarkup: keyboard,
                cancellationToken: ct
            );
            return;
        }
        if (msg?.Text == "/refresh")
        {
            var keyboard = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("🔄 Обновить", "refresh"));

            await _botClient.SendMessage(
                chatId: msg.ChatId,
                text: "Проверить актуальность данных?",
                replyMarkup: keyboard,
                cancellationToken: ct);
        }
        if (msg?.Text == "/status")
        {
            var keyboard = await BuildModeKeyboardAsync(ct);
            await _botClient.SendMessage(
                chatId: msg.ChatId,
                text: "Статусы ассистентов (нажмите, чтобы переключить):",
                replyMarkup: keyboard,
                cancellationToken: ct
            );
            return;
        }
        if (msg?.Text == "/urls")
        {
            var keyboard = await BuildUrlsKeyboardAsync();
            await _botClient.SendMessage(
                chatId: msg.ChatId,
                text: "Ссылки на треды",
                replyMarkup: keyboard,
                cancellationToken: ct
            );
            return;
        }
        if (msg?.Text == "/dates")
        {
            var settings = await _parserDateLimitBl.GetAllAsync(ct);
            var text = string.Join("\n", settings.Select(s => $"{s.ParserName}: {s.StopDate:dd.MM.yyyy}"));
            text = !string.IsNullOrEmpty(text) ? text : "В базе данных нет информации о датах";
            await _botClient.SendMessage(msg.ChatId, text, cancellationToken: ct);
            return;
        }
        if (msg?.Text == "/setdate")
        {
            var keyboard = BuildSetDateParsersKeyboard();
            await _botClient.SendMessage(msg.ChatId, "Выберите парсер:", replyMarkup: keyboard, cancellationToken: ct);
            return;
        }
        if (msg?.Text != null && msg.Text.StartsWith("/setdate"))
        {
            var parts = msg.Text.Split(' ');
            if (parts.Length == 3 && Enum.TryParse(parts[1], out ParserType _) &&
                DateOnly.TryParseExact(parts[2], DateOnlyFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                await _parserDateLimitBl.SetStopDateAsync(parts[1], dt, ct);
                await _botClient.SendMessage(msg.ChatId, $"Дата для {parts[1]} установлена на {dt:dd.MM.yyyy}", cancellationToken: ct);
            }
            else
            {
                await _botClient.SendMessage(msg.ChatId, "Неверный формат. /setdate <ParserName> dd.MM.yyyy", replyParameters: msg.MessageId, cancellationToken: ct);
            }
            return;
        }
        if (msg?.Text is "/a" or "/answer")
        {
            if (msg.ThreadId is null)
            {
                await _botClient.SendMessage(msg.ChatId,
                    "Команда доступна только внутри темы форума.", cancellationToken: ct);
                return;
            }

            _awaiting[msg.ThreadId.Value] = new(AwaitMode.Answer, DateTime.UtcNow);

            await _botClient.SendMessage(
                chatId: msg.ChatId,
                messageThreadId: msg.ThreadId,
                text: "Напишите корректирующий prompt для GPT:",
                cancellationToken: ct);

            return;
        }

        if (msg?.Text is "/c" or "/correcting")
        {
            if (msg.ThreadId is null)
            {
                await _botClient.SendMessage(msg.ChatId,
                    "Команда доступна только внутри темы форума.", cancellationToken: ct);
                return;
            }

            _awaiting[msg.ThreadId.Value] = new(AwaitMode.Correcting, DateTime.UtcNow);

            await _botClient.SendMessage(
                chatId: msg.ChatId,
                messageThreadId: msg.ThreadId,
                text: "Напишите новый запрос для GPT:",
                cancellationToken: ct);

            return;
        }

        if (msg?.Text != null && (msg.Text.StartsWith("/answer ") || msg.Text.StartsWith("/a ")))
        {
            await OnManualAnswerAsync(msg, ct);
        }
        if (msg?.Text != null && (msg.Text.StartsWith("/correcting ") || msg.Text.StartsWith("/c ")))
        {
            await OnCorrectingManualAnswer(msg, ct);
        }
        if (msg?.Text != null && msg.Text.StartsWith("/delete "))
        {
            await DeleteTopic(msg, ct);
        }
    }
    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        if (callbackQuery?.Data == null) return;

        await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);

        if (callbackQuery.Data is "await_a" or "await_c")
        {
            int threadId = callbackQuery.Message!.MessageThreadId!.Value;

            _awaiting[threadId] = new(
                callbackQuery.Data == "await_a" ? AwaitMode.Answer : AwaitMode.Correcting,
                DateTime.UtcNow);

            await _botClient.SendMessage(
                chatId: callbackQuery.Message.Chat.Id,
                messageThreadId: threadId,
                text: "Введите ваш запрос к GPT:",
                cancellationToken: ct);

            return;
        }

        if (callbackQuery.Data.StartsWith('/'))
        {
            await HandleMessageAsync(
                new TelegramMessageDto
                {
                    ChatId = callbackQuery.Message!.Chat.Id,
                    Text = callbackQuery.Data,
                    MessageId = callbackQuery.Message.MessageId,
                    ThreadId = callbackQuery.Message.MessageThreadId
                },
                ct);
        }
        if (callbackQuery.Data == "refresh")
        {
            await ManualTriggerRefreshAsync();
            return;
        }

        _callbackQueue.Writer.TryWrite(callbackQuery);
    }
    public async Task ManualTriggerRefreshAsync()
    {
        if (!await _refreshLock.WaitAsync(0))
        {
            Console.WriteLine("REFRESH уже запущен!");
            return;
        }

        try
        {
            await DoScheduledWorkAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background refresh failed");
        }
        finally
        {
            _refreshLock.Release();
        }
    }
    public async Task ProcessCallbackInBackgroundAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        var parts = callbackQuery!.Data!.Split("*_*", 2);
        if (parts.Length != 2)
            return;

        var action = parts[0];
        var payload = parts[1];

        if (action == "toggle")
        {
            await _assistantModeBl.ToggleModeAsync(payload, ct);

            var updatedKeyboard = await BuildModeKeyboardAsync(ct);
            await _botClient.EditMessageReplyMarkup(
                chatId: callbackQuery!.Message!.Chat.Id,
                messageId: callbackQuery.Message.MessageId,
                replyMarkup: updatedKeyboard,
                cancellationToken: ct
            );

            return;
        }
        else if (action == "parser")
        {
            if (!Enum.TryParse(payload, out ParserType parserType))
            {
                throw new Exception("Проверьте кнопку переключения статуса, передается не валидная строка");
            }

            await _parsersModeBl.ToggleModeAsync(parserType, ct);

            var updatedKeyboard = await BuildParsersKeyboardAsync(ct);
            await _botClient.EditMessageReplyMarkup(
                chatId: callbackQuery!.Message!.Chat.Id,
                messageId: callbackQuery.Message.MessageId,
                replyMarkup: updatedKeyboard,
                cancellationToken: ct
            );

            return;
        }
        else if (action == "setdate")
        {
            _awaitingDate[callbackQuery.From.Id] = payload;
            await _botClient.SendMessage(
                chatId: callbackQuery.Message!.Chat.Id,
                text: $"Введите дату для {payload} в формате dd.MM.yyyy:",
                cancellationToken: ct);

            return;
        }

        if (int.TryParse(payload, out var reqId))
        {
            var topic = await _topicRequestBl.GetByIdAsync(reqId);
            if (topic == null) return;

            switch (action)
            {
                case "apr":
                    await OnApproveAsync(callbackQuery, topic, ct);
                    break;
            }
        }
    }
    public async Task<bool> CheckIfLastMessageIsMenuAsync(CancellationToken ct)
    {
        var lastMessageType = await _lastMessageBL.GetLastMessageTypeAsync();
        if (lastMessageType == null || lastMessageType == LastMessageType.Message)
        {
            return false;
        }
        else
        {
            return true;
        }
    }
    public async Task<int> InitializeBotMenuAsync(CancellationToken stoppingToken)
    {
        var commands = new List<(string Command, string Description)>
        {
            ("/parsers", "Статусы парсеров"),
            ("/refresh", "Обновить данные"),
            ("/status", "Статусы ассистентов"),
            ("/urls", "Ссылки на треды"),
            ("/dates", "Вывод дат для парсеров"),
            ("/setdate", "Установить дату для парсера"),
        };

        var keyboardButtons = commands
            .Select(cmd => new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"{cmd.Command} – {cmd.Description}",
                    cmd.Command)
            })
            .ToArray();

        var keyboard = new InlineKeyboardMarkup(keyboardButtons);

        var message = await _botClient.SendMessage(
            chatId: _botConfig.ForumChatId,
            text: "Доступные команды:",
            replyMarkup: keyboard,
            cancellationToken: stoppingToken
        );

        return message.Id;
    }
    private async Task DeleteTopic(TelegramMessageDto message, CancellationToken ct)
    {
        var parts = message!.Text!.Trim().Split(' ');
        if (parts.Length != 1)
        {
            await _botClient.SendMessage(
                chatId: message.ChatId,
                text: "Неверный формат. Нужно: /delete",
                replyParameters: message.MessageId,
                cancellationToken: ct
            );
            return;
        }

        string requestId = message!.ThreadId!.Value.ToString();

        var topicRequest = await _topicRequestBl.TryGetTopicInfo(requestId);
        if (topicRequest != null)
        {
            await _botClient.DeleteForumTopic(
            chatId: message.ChatId,
            messageThreadId: message.ThreadId.Value,
            cancellationToken: ct
            );

            await _activeTopicBl.DeleteActiveTopicByMessageThreadIdAsync(message.ThreadId.Value.ToString());
        }
        else
        {
            await _botClient.SendMessage(
                chatId: message.ChatId,
                text: $"Не найдена заявка с ID {requestId}. Пожалуйста, проверьте правильность ввода.",
                replyParameters: message.MessageId,
                cancellationToken: ct
            );
        }
    }
    private async Task OnApproveAsync(CallbackQuery callbackQuery, TopicRequest topic, CancellationToken ct)
    {
        topic.Status = "Approved";
        await _topicRequestBl.UpdateAsync(topic);

        await _botClient.SendMessage(
            chatId: callbackQuery!.Message!.Chat.Id,
            messageThreadId: callbackQuery.Message.MessageThreadId,
            text: $"Заявка {topic.RequestId}: черновой ответ одобрен и отправлен пользователю.",
            cancellationToken: ct
        );

        _answerClientService.SendMessageToClientManualAssistant(topic);

        await _botClient.DeleteForumTopic(
            chatId: callbackQuery.Message.Chat.Id,
            messageThreadId: callbackQuery!.Message!.MessageThreadId!.Value,
            cancellationToken: ct
        );

        await _activeTopicBl.DeleteActiveTopicByMessageThreadIdAsync(callbackQuery!.Message!.MessageThreadId.ToString()!);
    }
    private async Task OnCorrectingManualAnswer(TelegramMessageDto message, CancellationToken ct)
    {
        var parts = message.Text!.Split(' ');
        if (parts.Length < 2)
        {
            await _botClient.SendMessage(
                chatId: message.ChatId,
                text: "Неверный формат. Нужно: /correcting(или /c) <текст ответа>",
                replyParameters: message.MessageId,
                cancellationToken: ct
            );
            return;
        }

        int threadId = message.ThreadId!.Value;
        string manualAnswer = string.Join(' ', parts.Skip(1));

        var topicRequest = await _topicRequestBl.TryGetTopicInfoByThreadId(threadId);
        if (topicRequest != null)
        {
            var messageForGpt = manualAnswer
                .Replace("COMMENT", topicRequest.UserQuestion)
                .Replace("GPTANSWER", topicRequest.GptDraftAnswer);


            await _botClient.SendMessage(
                chatId: _botConfig.ForumChatId,
                messageThreadId: threadId,
                text: $"Ручной ответ для заявки {threadId}:\n\n{messageForGpt}",
                cancellationToken: ct
            );

            var gptAnswer = await _chatGPTClient.SendMessageAsync(messageForGpt, topicRequest.AssistantType, ct);
            await _topicRequestBl.UpdateGptDraftAnswer(topicRequest.Id, gptAnswer);

            await _correctAnswerBl.AddAsync(new CorrectAnswer
            {
                UserMessage = topicRequest.UserQuestion,
                GptAnswer = topicRequest.GptDraftAnswer,
                AdminAnswer = manualAnswer
            });

            var inlineKeyboard = new InlineKeyboardMarkup(
        [
            [
                InlineKeyboardButton.WithCallbackData("Да", $"apr*_*{topicRequest.Id}"),
                InlineKeyboardButton.WithCallbackData("✅ Корректирующий /a", "await_a"),
                InlineKeyboardButton.WithCallbackData("✏️ Новый запрос /c",  "await_c")
            ]
        ]);

            await _botClient.SendMessage(
                chatId: _botConfig.ForumChatId,
                messageThreadId: threadId,
                text: $"**Черновой ответ ChatGPT**:\n\n{gptAnswer}",
                parseMode: ParseMode.Markdown,
                replyMarkup: inlineKeyboard,
                cancellationToken: ct
            );
        }
        else
        {
            await _botClient.SendMessage(
                chatId: message.ChatId,
                text: $"Не найдена заявка с ID {threadId}",
                replyParameters: message.MessageId,
                cancellationToken: ct
            );
        }
    }
    private async Task OnManualAnswerAsync(TelegramMessageDto message, CancellationToken ct)
    {
        var parts = message.Text!.Split(' ');
        if (parts.Length < 2)
        {
            await _botClient.SendMessage(
                chatId: message.ChatId,
                text: "Неверный формат. Нужно: /answer(или /a) <текст ответа>",
                replyParameters: message.MessageId,
                cancellationToken: ct
            );
            return;
        }

        int threadId = message.ThreadId!.Value;
        string manualAnswer = string.Join(' ', parts.Skip(1));

        var topicRequest = await _topicRequestBl.TryGetTopicInfoByThreadId(threadId);
        if (topicRequest != null)
        {
            var messageForGpt = _answerPrompt?
                .Replace("RIGHTANSWER", manualAnswer)
                .Replace("COMMENT", topicRequest.UserQuestion)
                .Replace("GPTANSWER", topicRequest.GptDraftAnswer);

            await _botClient.SendMessage(
                chatId: _botConfig.ForumChatId,
                messageThreadId: threadId,
                text: $"Ручной ответ для заявки {threadId}:\n\n{manualAnswer}\n\nПромт:\n{messageForGpt}",
                cancellationToken: ct
            );

            var gptAnswer = await _chatGPTClient.SendMessageAsync(messageForGpt!, topicRequest.AssistantType, ct);
            await _topicRequestBl.UpdateGptDraftAnswer(topicRequest.Id, gptAnswer);

            await _correctAnswerBl.AddAsync(new CorrectAnswer
            {
                UserMessage = topicRequest.UserQuestion,
                GptAnswer = gptAnswer,
                AdminAnswer = manualAnswer
            });

            var inlineKeyboard = new InlineKeyboardMarkup(
        [
            [
                InlineKeyboardButton.WithCallbackData("Да", $"apr*_*{topicRequest.Id}"),
                InlineKeyboardButton.WithCallbackData("✅ Корректирующий /a", "await_a"),
                InlineKeyboardButton.WithCallbackData("✏️ Новый запрос /c",  "await_c")
            ]
        ]);

            await _botClient.SendMessage(
                chatId: _botConfig.ForumChatId,
                messageThreadId: threadId,
                text: $"**Черновой ответ ChatGPT**:\n\n{gptAnswer}",
                parseMode: ParseMode.Markdown,
                replyMarkup: inlineKeyboard,
                cancellationToken: ct
            );
        }
        else
        {
            await _botClient.SendMessage(
                chatId: message.ChatId,
                text: $"Не найдена заявка с ID {threadId}. Пожалуйста, проверьте правильность ввода.",
                replyParameters: message.MessageId,
                cancellationToken: ct
            );
        }
    }
    private async Task<InlineKeyboardMarkup> BuildParsersKeyboardAsync(CancellationToken ct)
    {
        var modes = await _parsersModeBl.GetAllModesAsync(ct);
        var buttons = modes
            .Select(kvp => InlineKeyboardButton.WithCallbackData(
                text: $"{kvp.ParserName}: {(kvp.IsActive ? "Активен" : "Неактивен")}",
                callbackData: $"parser*_*{kvp.ParserName}"
            ))
            .Select(btn => new[] { btn })
            .ToArray();

        return new InlineKeyboardMarkup(buttons);
    }
    private static InlineKeyboardMarkup BuildSetDateParsersKeyboard()
    {
        var buttons = Enum.GetNames(typeof(ParserType))
            .Select(name => InlineKeyboardButton.WithCallbackData(name, $"setdate*_*{name}"))
            .Select(btn => new[] { btn })
            .ToArray();

        return new InlineKeyboardMarkup(buttons);
    }
    private Task<InlineKeyboardMarkup> BuildUrlsKeyboardAsync()
    {
        var buttons = _URLis
            .Select(kvp => new InlineKeyboardButton(
                text: $"{kvp.Key}: {kvp.Value}",
                callbackDataOrUrl: $"{kvp.Value}"
            ))
            .Select(btn => new[] { btn })
            .ToArray();

        return Task.FromResult(new InlineKeyboardMarkup(buttons));
    }
    private async Task<InlineKeyboardMarkup> BuildModeKeyboardAsync(CancellationToken ct)
    {
        var modes = await _assistantModeBl.GetAllModesAsync(ct);
        var buttons = modes
            .Select(kvp => InlineKeyboardButton.WithCallbackData(
                text: $"{kvp.AssistantName}: {(kvp.IsAuto ? "Авто" : "Ручной")}",
                callbackData: $"toggle*_*{kvp.AssistantName}"
            ))
            .Select(btn => new[] { btn })
            .ToArray();

        return new InlineKeyboardMarkup(buttons);
    }
    public async Task DoScheduledWorkAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("Начинаю обновление существующих топиков");
        await RefreshAllTopicsByAllRefresh(stoppingToken);

        var dto = await RefreshProcessedEntities();

        Console.WriteLine("Начинаю обновление потенциальных топиков");
        _refreshTopicsService.RefreshTopics(dto, stoppingToken);

        Console.WriteLine($"REFRESH ЗАВЕРШЕН В {DateTime.UtcNow}");
    }
    private async Task<RefreshTopicDto> RefreshProcessedEntities()
    {
        var activeTopics = await _activeTopicBl.GetActiveTopicsAsync();
        var potentialTopics = await _dataRepositoryBl.GetUnprocessedEntriesAsync();

        var questionsIds = await ExtractQuestionsIds(activeTopics, potentialTopics);
        var reviewsIds = await ExtractReviewsIds(activeTopics, potentialTopics);

        var dto = new RefreshTopicDto
        {
            RequestIdsForQuestion = questionsIds!,
            RequestIdsForReviews = reviewsIds!
        };

        return dto;
    }
    private static Task<IEnumerable<string>?> ExtractReviewsIds(IEnumerable<ActiveTopic> activeTopics, IEnumerable<NewDataEntry> potentialTopics)
    {
        var reviewsIdsByActiveTopics = activeTopics.Where(x => x.ParserName == "ReviewsParser").Select(topic => topic.RequestId);
        var reviewsIdsByPotentialTopics = potentialTopics.Where(x => x.ParserName == "ReviewsParser").Select(x => x.SourceRecordId);
        var reviewsIds = reviewsIdsByActiveTopics.Union(reviewsIdsByPotentialTopics);
        return Task.FromResult<IEnumerable<string>?>(reviewsIds);
    }
    private static Task<IEnumerable<string>?> ExtractQuestionsIds(IEnumerable<ActiveTopic> activeTopics, IEnumerable<NewDataEntry> potentialTopics)
    {
        var questionsIdsByActiveTopics = activeTopics.Where(x => x.ParserName == "QuestionsParserApp").Select(topic => topic.RequestId);
        var questionsIdsByPotentialTopics = potentialTopics.Where(x => x.ParserName == "QuestionsParserApp").Select(x => x.SourceRecordId);
        var questionsIds = questionsIdsByActiveTopics.Union(questionsIdsByPotentialTopics);
        return Task.FromResult<IEnumerable<string>?>(questionsIds);
    }
    private async Task RefreshAllTopicsByAllRefresh(CancellationToken ct)
    {
        var topics = await _activeTopicBl.GetActiveTopicsAsync();
        foreach (var topic in topics)
        {
            try
            {
                var data = await ParseDataFromRequestId(topic);

                _logger.LogInformation("Refresh: {findClientName} - {findDate:dd.MM.yyyy} - {findTime} - {parserName}", data.FindClientName, data.FindDate, data.FindTime, topic.ParserName);

                var refresh = _answerClientService.RefreshTopic(topic);

                _logger.LogInformation("{refresh} удаление топика {findClientName} - {findDate:dd.MM.yyyy} - {findTime}", refresh, data.FindClientName, data.FindDate, data.FindTime);
                if (refresh == true)
                {
                    await _botClient.DeleteForumTopic(
                        chatId: _botConfig.ForumChatId,
                        messageThreadId: int.Parse(topic.MessageThreadId),
                        cancellationToken: ct
                        );
                    await _activeTopicBl.DeleteActiveTopicAsync(topic);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Произошла ошибка во время обновления существующего топика");
            }
        }
        _logger.LogInformation("ОБНОВЛЕНИЕ СУЩЕСТВУЮЩИХ ТОПИКОВ ЗАВЕРШЕНО");
    }
    private Task<DataFromRequestIdDto> ParseDataFromRequestId(ActiveTopic activeTopic)
    {
        if (activeTopic.ParserName == "ChatParserApp")
        {
            var temp = _chatParserBl.GetChatRecordByChatId(activeTopic.RequestId);
            return Task.FromResult(new DataFromRequestIdDto
            {
                FindDate = temp.Date,
                FindTime = null,
                FindClientName = temp.Title
            });
        }
        else
        {
            var temp = activeTopic.RequestId.Split('_');
            return Task.FromResult(new DataFromRequestIdDto
            {
                FindDate = DateOnly.ParseExact(temp[1], DateOnlyFormat, CultureInfo.InvariantCulture),
                FindTime = TimeOnly.Parse(temp[2]),
                FindClientName = temp[3]
            });
        }
    }
}
