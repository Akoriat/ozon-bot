using Bl.Common.Configs;
using Bl.Common.DTOs;
using Bl.Common.Enum;
using Bl.Implementations;
using Bl.Interfaces;
using DAL.Models;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TL;
using Message = Telegram.Bot.Types.Message;
using Update = Telegram.Bot.Types.Update;

namespace Ozon.Bot.Services
{
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

        private readonly Channel<CallbackQuery> _callbackQueue;
        private readonly Dictionary<string, string> _urls;
        private readonly string? _answerPrompt;
        private static readonly SemaphoreSlim _refreshLock = new(1, 1);
        private enum AwaitMode { None, Answer, Correcting }

        private record AwaitState(AwaitMode Mode, DateTime SetAt);

        private readonly ConcurrentDictionary<int, AwaitState> _awaiting = new();

        private const int AwaitTimeoutMinutes = 10;


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
            IOptions<UrlsConfig> urlsOptions,
            IOptions<PromtsConfig> promtsOptions,
            IParsersModeBl parsersModeBl,
            Channel<CallbackQuery> callbackQueue)
        {
            _botClient = botClient;
            _botConfig = botConfig.Value;
            _urls = new Dictionary<string, string>
                {
                    { nameof(urlsOptions.Value.ReviewGood), urlsOptions.Value.ReviewGood },
                    { nameof(urlsOptions.Value.ReviewBad), urlsOptions.Value.ReviewBad },
                    { nameof(urlsOptions.Value.ChatGeneral), urlsOptions.Value.ChatGeneral },
                    { nameof(urlsOptions.Value.QuestionsOthers), urlsOptions.Value.QuestionsOthers },
                    { nameof(urlsOptions.Value.QuestionsBr), urlsOptions.Value.QuestionsBr },
                    { nameof(urlsOptions.Value.QuestionsCh), urlsOptions.Value.QuestionsCh },
                    { nameof(urlsOptions.Value.QuestionsKr), urlsOptions.Value.QuestionsKr },
                    { nameof(urlsOptions.Value.QuestionsDsOrDn), urlsOptions.Value.QuestionsDsOrDn }
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
            _answerPrompt = promtsOptions.Value.AnswerPrompt;
            _parsersModeBl = parsersModeBl;
            _callbackQueue = callbackQueue;
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
                        await HandleMessageAsync(update.Message!, ct);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Update handling failed: {UpdateId}", update.Id);
            }
        }

        private async Task HandleMessageAsync(Message msg, CancellationToken ct)
        {
            // 1) удаляем устаревшие записи
            foreach (var (thread, state) in _awaiting)
                if (state.SetAt < DateTime.UtcNow.AddMinutes(-AwaitTimeoutMinutes))
                    _awaiting.TryRemove(thread, out _);

            // 2) ловим ожидаемый текст (не начинается с '/')
            if (msg.MessageThreadId is int tid &&
                _awaiting.TryRemove(tid, out var st) &&
                !msg.Text!.StartsWith("/"))
            {
                if (st.Mode == AwaitMode.Answer)
                    msg.Text = "/answer " + msg.Text;
                else
                    msg.Text = "/correcting " + msg.Text;

                if (st.Mode == AwaitMode.Answer)
                    await OnManualAnswerAsync(msg, ct);
                else
                    await OnCorrectingManualAnswer(msg, ct);

                return;          // уже обработали
            }
            if (msg?.Text == "/parsers")
            {
                var keyboard = await BuildParsersKeyboardAsync(ct);
                await _botClient.SendMessage(
                    chatId: msg.Chat.Id,
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
                    chatId: msg.Chat.Id,
                    text: "Проверить актуальность данных?",
                    replyMarkup: keyboard,
                    cancellationToken: ct);
            }
            if (msg?.Text == "/status")
            {
                var keyboard = await BuildModeKeyboardAsync(ct);
                await _botClient.SendMessage(
                    chatId: msg.Chat.Id,
                    text: "Статусы ассистентов (нажмите, чтобы переключить):",
                    replyMarkup: keyboard,
                    cancellationToken: ct
                );
                return;
            }
            if (msg?.Text == "/urls")
            {
                var keyboard = await BuildUrlsKeyboardAsync(ct);
                await _botClient.SendMessage(
                    chatId: msg.Chat.Id,
                    text: "Ссылки на треды",
                    replyMarkup: keyboard,
                    cancellationToken: ct
                );
                return;
            }
            if (msg?.Text is "/a" or "/answer")
            {
                if (msg.MessageThreadId is null)
                {
                    await _botClient.SendMessage(msg.Chat.Id,
                        "Команда доступна только внутри темы форума.", cancellationToken: ct);
                    return;
                }

                _awaiting[msg.MessageThreadId.Value] = new(AwaitMode.Answer, DateTime.UtcNow);

                await _botClient.SendMessage(
                    chatId: msg.Chat.Id,
                    messageThreadId: msg.MessageThreadId,
                    text: "Напишите корректирующий prompt для GPT:",
                    cancellationToken: ct);

                return;
            }

            if (msg?.Text is "/c" or "/correcting")
            {
                if (msg.MessageThreadId is null)
                {
                    await _botClient.SendMessage(msg.Chat.Id,
                        "Команда доступна только внутри темы форума.", cancellationToken: ct);
                    return;
                }

                _awaiting[msg.MessageThreadId.Value] = new(AwaitMode.Correcting, DateTime.UtcNow);

                await _botClient.SendMessage(
                    chatId: msg.Chat.Id,
                    messageThreadId: msg.MessageThreadId,
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

            if (callbackQuery.Data == "refresh")
            {
                ManualTriggerRefresh();
                return;
            }

            _callbackQueue.Writer.TryWrite(callbackQuery);
        }

        public void ManualTriggerRefresh()
        {
            _ = Task.Run(async () =>
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
            });
        }

        public async Task ProcessCallbackInBackgroundAsync(CallbackQuery callbackQuery, CancellationToken ct)
        {
            var parts = callbackQuery.Data.Split("*_*", 2);
            if (parts.Length != 2)
                return;

            var action = parts[0];
            var payload = parts[1];

            if (action == "toggle")
            {
                if (!Enum.TryParse(payload, out AssistantType assistantType))
                {
                    throw new Exception("Проверьте кнопку переключения статуса, передается не валидная строка");
                }

                bool newMode = await _assistantModeBl.ToggleModeAsync(assistantType, ct);

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

                bool newMode = await _parsersModeBl.ToggleModeAsync(parserType, ct);

                var updatedKeyboard = await BuildParsersKeyboardAsync(ct);
                await _botClient.EditMessageReplyMarkup(
                    chatId: callbackQuery!.Message!.Chat.Id,
                    messageId: callbackQuery.Message.MessageId,
                    replyMarkup: updatedKeyboard,
                    cancellationToken: ct
                );

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
        private async Task DeleteTopic(Message message, CancellationToken ct)
        {
            var parts = message!.Text!.Trim().Split(' ');
            if (parts.Length != 1)
            {
                await _botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Неверный формат. Нужно: /delete",
                    replyParameters: message.MessageId,
                    cancellationToken: ct
                );
                return;
            }

            string requestId = message!.MessageThreadId!.Value.ToString();

            var topicRequest = await _topicRequestBl.TryGetTopicInfo(requestId);
            if (topicRequest != null)
            {
                await _botClient.DeleteForumTopic(
                chatId: message.Chat.Id,
                messageThreadId: message.MessageThreadId.Value,
                cancellationToken: ct
                );

                await _activeTopicBl.DeleteActiveTopicByMessageThreadIdAsync(message.MessageThreadId.Value.ToString());
            }
            else
            {
                await _botClient.SendMessage(
                    chatId: message.Chat.Id,
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
                chatId: callbackQuery.Message.Chat.Id,
                messageThreadId: callbackQuery.Message.MessageThreadId,
                text: $"Заявка {topic.RequestId}: черновой ответ одобрен и отправлен пользователю.",
                cancellationToken: ct
            );

            _answerClientService.SendMessageToClientManualAssistant(topic);

            await _botClient.DeleteForumTopic(
                chatId: callbackQuery.Message.Chat.Id,
                messageThreadId: callbackQuery.Message.MessageThreadId.Value,
                cancellationToken: ct
            );

            await _activeTopicBl.DeleteActiveTopicByMessageThreadIdAsync(callbackQuery.Message.MessageThreadId.ToString());
        }

        //private async Task OnRejectAsync(CallbackQuery callbackQuery, TopicRequest topic, CancellationToken ct)
        //{
        //    topic.Status = "Rejected";
        //    await _topicRequestBl.UpdateAsync(topic);

        //    await _botClient.AnswerCallbackQuery(
        //    callbackQuery.Id,
        //        $"Ответ для заявки {topic.RequestId} отклонён. Введите корректный ответ.",
        //        cancellationToken: ct
        //    );

        //    await _botClient.SendMessage(
        //        chatId: callbackQuery.Message.Chat.Id,
        //        messageThreadId: callbackQuery.Message.MessageThreadId,
        //        text: $"Заявка {topic.RequestId}: черновой ответ отклонён.\nНапишите правильный ответ командой:\n`/answer(или /a) Ваш текст...`",
        //        cancellationToken: ct
        //    );
        //}
        //private async Task OnCorrectingAsync(CallbackQuery callbackQuery, TopicRequest topic, CancellationToken ct)
        //{
        //    topic.Status = "Correcting";
        //    await _topicRequestBl.UpdateAsync(topic);

        //    await _botClient.AnswerCallbackQuery(
        //    callbackQuery.Id,
        //        $"Ответ для заявки {topic.RequestId} корректируется. Введите корректный ответ.",
        //        cancellationToken: ct
        //    );

        //    await _botClient.SendMessage(
        //        chatId: callbackQuery.Message.Chat.Id,
        //        messageThreadId: callbackQuery.Message.MessageThreadId,
        //        text: $"Заявка {topic.RequestId}: черновой ответ корректируется.\nНапишите правильный ответ командой:\n`/correcting(или /c) Ваш текст...`",
        //        cancellationToken: ct
        //    );

        //    await _botClient.SendMessage(
        //        chatId: callbackQuery.Message.Chat.Id,
        //        messageThreadId: callbackQuery.Message.MessageThreadId,
        //        text: $"В своем ответе можете использовать переменные {{UserQuestion}} - вопрос/отзыв клиента и {{GptDraftAnswer}} - предыдущий ответ чат гпт",
        //        cancellationToken: ct
        //    );
        //}
        private async Task OnCorrectingManualAnswer(Message message, CancellationToken ct)
        {
            var parts = message.Text.Split(' ');
            if (parts.Length < 2)
            {
                await _botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Неверный формат. Нужно: /correcting(или /c) <текст ответа>",
                    replyParameters: message.MessageId,
                    cancellationToken: ct
                );
                return;
            }

            int threadId = message.MessageThreadId.Value;
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

                var gptAnswer = await _chatGPTClient.SendMessageAsync(messageForGpt, (AssistantType)topicRequest.AssistantType, ct);
                await _topicRequestBl.UpdateGptDraftAnswer(topicRequest.Id, gptAnswer);

                await _correctAnswerBl.AddAsync(new CorrectAnswer
                {
                    UserMessage = topicRequest.UserQuestion,
                    GptAnswer = topicRequest.GptDraftAnswer,
                    AdminAnswer = manualAnswer
                });

                var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new [] {
                    InlineKeyboardButton.WithCallbackData("Да", $"apr*_*{topicRequest.Id}"),
                    InlineKeyboardButton.WithCallbackData("✅ Корректирующий /a", "await_a"),
                    InlineKeyboardButton.WithCallbackData("✏️ Новый запрос /c",  "await_c")
                }
            });

                await _botClient.SendMessage(
                    chatId: _botConfig.ForumChatId,
                    messageThreadId: threadId,
                    text: $"**Черновой ответ ChatGPT**:\n\n{gptAnswer}",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                    replyMarkup: inlineKeyboard,
                    cancellationToken: ct
                );
            }
            else
            {
                await _botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: $"Не найдена заявка с ID {threadId}",
                    replyParameters: message.MessageId,
                    cancellationToken: ct
                );
            }
        }

        private async Task OnManualAnswerAsync(Message message, CancellationToken ct)
        {
            var parts = message.Text.Split(' ');
            if (parts.Length < 2)
            {
                await _botClient.SendMessage(
                    chatId: message.Chat.Id,
                    text: "Неверный формат. Нужно: /answer(или /a) <текст ответа>",
                    replyParameters: message.MessageId,
                    cancellationToken: ct
                );
                return;
            }

            int threadId = message.MessageThreadId.Value;
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

                var gptAnswer = await _chatGPTClient.SendMessageAsync(messageForGpt, (AssistantType)topicRequest.AssistantType, ct);
                await _topicRequestBl.UpdateGptDraftAnswer(topicRequest.Id, gptAnswer);

                await _correctAnswerBl.AddAsync(new CorrectAnswer
                {
                    UserMessage = topicRequest.UserQuestion,
                    GptAnswer = gptAnswer,
                    AdminAnswer = manualAnswer
                });

                var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new [] {
                    InlineKeyboardButton.WithCallbackData("Да", $"apr*_*{topicRequest.Id}"),
                    InlineKeyboardButton.WithCallbackData("✅ Корректирующий /a", "await_a"),
                    InlineKeyboardButton.WithCallbackData("✏️ Новый запрос /c",  "await_c")
                }
            });

                await _botClient.SendMessage(
                    chatId: _botConfig.ForumChatId,
                    messageThreadId: threadId,
                    text: $"**Черновой ответ ChatGPT**:\n\n{gptAnswer}",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                    replyMarkup: inlineKeyboard,
                    cancellationToken: ct
                );

                //_answerClientService.SendMessageToClientManualAssistant(topicRequest, manualAnswer);

                //await _botClient.DeleteForumTopic(
                //chatId: message.Chat.Id,
                //messageThreadId: message.MessageThreadId.Value,
                //cancellationToken: ct
                //);

                //await _activeTopicBl.DeleteActiveTopicByMessageThreadIdAsync(message.MessageThreadId.Value.ToString());
            }
            else
            {
                await _botClient.SendMessage(
                    chatId: message.Chat.Id,
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
        private async Task<InlineKeyboardMarkup> BuildUrlsKeyboardAsync(CancellationToken ct)
        {
            var buttons = _urls
                .Select(kvp => new InlineKeyboardButton(
                    text: $"{kvp.Key}: {kvp.Value}",
                    callbackDataOrUrl: $"{kvp.Value}"
                ))
                .Select(btn => new[] { btn })
                .ToArray();

            return new InlineKeyboardMarkup(buttons);
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

            var dto = await RefreshProcessedEntities(stoppingToken);

            Console.WriteLine("Начинаю обновление потенциальных топиков");
            _refreshTopicsService.RefreshTopics(dto, stoppingToken);

            Console.WriteLine($"REFRESH ЗАВЕРШЕН В {DateTime.UtcNow}");
        }

        private async Task<RefreshTopicDto> RefreshProcessedEntities(CancellationToken ct)
        {
            var activeTopics = await _activeTopicBl.GetActiveTopicsAsync();
            var potentialTopics = await _dataRepositoryBl.GetUnprocessedEntriesAsync();

            var questionsIds = await ExtractQuestionsIds(activeTopics, potentialTopics, ct);
            var reviewsIds = await ExtractReviewsIds(activeTopics, potentialTopics, ct);

            var dto = new RefreshTopicDto
            {
                RequestIdsForQuestion = questionsIds,
                RequestIdsForReviews = reviewsIds
            };

            return dto;
        }

        private async Task<IEnumerable<string>?> ExtractReviewsIds(IEnumerable<ActiveTopic> activeTopics, IEnumerable<NewDataEntry> potentialTopics, CancellationToken ct)
        {
            var reviewsIdsByActiveTopics = activeTopics.Where(x => x.ParserName == "ReviewsParser").Select(topic => topic.RequestId);
            var reviewsIdsByPotentialTopics = potentialTopics.Where(x => x.ParserName == "ReviewsParser").Select(x => x.SourceRecordId);
            var reviewsIds = reviewsIdsByActiveTopics.Union(reviewsIdsByPotentialTopics);
            return reviewsIds;
        }

        private async Task<IEnumerable<string>?> ExtractQuestionsIds(IEnumerable<ActiveTopic> activeTopics, IEnumerable<NewDataEntry> potentialTopics, CancellationToken ct)
        {
            var questionsIdsByActiveTopics = activeTopics.Where(x => x.ParserName == "QuestionsParserApp").Select(topic => topic.RequestId);
            var questionsIdsByPotentialTopics = potentialTopics.Where(x => x.ParserName == "QuestionsParserApp").Select(x => x.SourceRecordId);
            var questionsIds = questionsIdsByActiveTopics.Union(questionsIdsByPotentialTopics);
            return questionsIds;
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

        private async Task<DataFromRequestIdDto> ParseDataFromRequestId(ActiveTopic activeTopic)
        {
            if (activeTopic.ParserName == "ChatParserApp")
            {
                var temp = _chatParserBl.GetChatRecordByChatId(activeTopic.RequestId);
                return new DataFromRequestIdDto
                {
                    FindDate = temp.Date,
                    FindTime = null,
                    FindClientName = temp.Title
                };
            }
            else
            {
                var temp = activeTopic.RequestId.Split('_');
                return new DataFromRequestIdDto
                {
                    FindDate = DateOnly.ParseExact(temp[1], "dd.MM.yyyy", CultureInfo.InvariantCulture),
                    FindTime = TimeOnly.Parse(temp[2]),
                    FindClientName = temp[3]
                };
            }
        }

        private static InlineKeyboardMarkup BuildAwaitKeyboard() =>
    new(new[]
    {
        new[]
        {
            InlineKeyboardButton.WithCallbackData("✅ Корректирующий /a", "await_a"),
            InlineKeyboardButton.WithCallbackData("✏️ Новый запрос /c",  "await_c")
        }
    });
    }
}
