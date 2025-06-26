using Bl.Common.Configs;
using Bl.Common.DTOs;
using Bl.Interfaces;
using DAL.Models;
using Microsoft.Extensions.Options;
using System.Threading;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TL;

namespace Ozon.Bot.Services
{
    public class ForumTopicService : IForumTopicService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly IChatGPTClient _chatGPTClient;
        private readonly ITopicRequestBl _topicRequestBl;
        private readonly IAssistantModeBl _assistantModeBl;
        private readonly IAnswerClientService _answerClientService;
        private readonly IActiveTopicBl _activeTopicBl;
        private readonly long _forumChatId;

        public ForumTopicService(ITelegramBotClient botClient
            , IOptions<BotConfig> botConfig
            , IChatGPTClient chatGPTClient
            , ITopicRequestBl topicRequestBl
            , IAssistantModeBl assistantModeBl
            , IAnswerClientService answerClientService
            ,
IActiveTopicBl activeTopicBl)
        {
            _botClient = botClient;
            _forumChatId = botConfig.Value.ForumChatId;
            _chatGPTClient = chatGPTClient;
            _topicRequestBl = topicRequestBl;
            _assistantModeBl = assistantModeBl;
            _answerClientService = answerClientService;
            _activeTopicBl = activeTopicBl;
        }

        public async Task CreateTopicForRequestAsync(CreateTopicDto createTopicDto, CancellationToken ct)
        {
            // avoid duplicates if the request has been processed earlier
            var existing = await _topicRequestBl.TryGetTopicInfo(createTopicDto.RequestId);
            if (existing != null)
            {
                return; // topic already exists
            }

            var activeTopics = await _activeTopicBl.GetActiveTopicsAsync();
            if (activeTopics.Any(x => x.RequestId == createTopicDto.RequestId))
            {
                return;
            }

            var assistantMods = await _assistantModeBl.GetAllModesAsync(ct);
            var modWithCurrentName = assistantMods.FirstOrDefault(x => x.AssistantName == createTopicDto.AssistantType.ToString());
            if (modWithCurrentName == null)
            {
                throw new Exception($"CreateTopicForRequestAsync в ForumTopicService: ассистент с именем {createTopicDto.AssistantType.ToString()} не найден");
            }
            switch (modWithCurrentName.IsAuto)
            {
                case true:
                    await ExecuteAutoAssistant(createTopicDto, ct);
                    break;
                case false:
                    await ExecuteManualAssistant(createTopicDto, ct);
                    break;
            }

        }
        private async Task ExecuteAutoAssistant(CreateTopicDto createTopicDto, CancellationToken ct)
        {
            var gptDraftAnswer = await _chatGPTClient.SendMessageAsync(
                userMessage: createTopicDto.ForChatGpt,
                assistantType: createTopicDto.AssistantType,
            ct
            );

            var dto = new SendMessageToClientAutoAssistantDto
            {
                RequestId = createTopicDto.RequestId,
                ParserName = createTopicDto.ParserName,
                GptDraftAnswer = gptDraftAnswer,
            };

            _answerClientService.SendMessageToClientAutoAssistant(dto);
        }
        private async Task ExecuteManualAssistant(CreateTopicDto createTopicDto, CancellationToken ct)
        {
            var gptDraftAnswer = await _chatGPTClient.SendMessageAsync(
                userMessage: createTopicDto.ForChatGpt,
                assistantType: createTopicDto.AssistantType,
                ct
                );

            var forumTopic = await _botClient.CreateForumTopic(
                chatId: _forumChatId,
                name: createTopicDto.TopicName!,
                cancellationToken: ct
            );

            var threadId = forumTopic.MessageThreadId;

            var topicRequest = new TopicRequest
            {
                RequestId = createTopicDto.RequestId,
                MessageThreadId = threadId,
                UserQuestion = createTopicDto.UserQuestion,
                GptDraftAnswer = gptDraftAnswer,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                ParserName = createTopicDto.ParserName,
                AssistantType = (int)createTopicDto.AssistantType
            };

            var topicRequestId = await _topicRequestBl.AddAsync(topicRequest);

            await _activeTopicBl.AddActiveTopicAsync(new ActiveTopic
            {
                RequestId = topicRequest.RequestId,
                AssistantType = topicRequest.AssistantType,
                ParserName = topicRequest.ParserName,
                MessageThreadId = topicRequest.MessageThreadId.ToString(),
                Article = createTopicDto.Article,
            });



            await _botClient.SendMessage(
                chatId: _forumChatId,
                messageThreadId: threadId,
                text: $"История сообщений:\n\n{createTopicDto.FullChat}",
                cancellationToken: ct
            );

            await _botClient.SendMessage(
                chatId: _forumChatId,
                messageThreadId: threadId,
                text: $"Сообщение от пользователя" +
                $"\nТип ассистента = {createTopicDto.AssistantType}" +
                $"\nRequestId = {createTopicDto.RequestId}" +
                $"\nПродукт(если есть) = {createTopicDto.Product}" +
                $"\nИмя клиента = {createTopicDto.ClientName}" +
                $"\nОценка(если есть) = {createTopicDto.Rating}" +
                $"\nФото = {createTopicDto.Photo}" +
                $"\nВидео = {createTopicDto.Video}" +
                $"\nАртикул(если есть) = {createTopicDto.Article}" +
                $":\n\n{createTopicDto.UserQuestion}",
                cancellationToken: ct
            );

            //await _botClient.SendMessage(
            //     chatId: _forumChatId,
            //     messageThreadId: threadId,
            //     text: $"/a или /answer - это отправка корректирующего ответа в гпт с использованием промта из конфигурационного файла. Возвращает исправленный гпт ответ.\n/c или /correcting - отправка сообщения в гпт, ничем не отличается от простого написания в чат с гпт, возвращает ответ гпт",
            //     cancellationToken: ct
            // );

            // await _botClient.SendMessage(
            //     chatId: _forumChatId,
            //     messageThreadId: threadId,
            //     text: $"Переменные:\nGPTANSWER - Предыдущий ответ GPT, если есть\nCOMMENT - Комментарий клиента",
            //     cancellationToken: ct
            // );

            await _botClient.SendMessage(
                chatId: _forumChatId,
                messageThreadId: threadId,
                text: "Выберите, как хотите продолжить работу с GPT.\n\n" +
                "• *Корректирующий* — правим черновой ответ, используя системный промпт.\n" +
                "• *Новый запрос* — задаём свой вопрос GPT с нуля.\n\n" +
                "_Переменные:_\n" +
                "`GPTANSWER` — предыдущий ответ GPT (если был)\n" +
                "`COMMENT`   — комментарий клиента",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                replyMarkup: BuildAwaitKeyboard(),
                cancellationToken: ct
                 );

            var inlineKeyboard = new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(new[]
            {
                new [] {
                    InlineKeyboardButton.WithCallbackData("Отправить ответ", $"apr*_*{topicRequestId}"),
                }
            });

            await _botClient.SendMessage(
                chatId: _forumChatId,
                messageThreadId: threadId,
                text: $"**Черновой ответ ChatGPT**:\n\n{gptDraftAnswer}",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                replyMarkup: inlineKeyboard,
                cancellationToken: ct
            );
        }

        private static InlineKeyboardMarkup BuildAwaitKeyboard() =>
    new InlineKeyboardMarkup(new[]
    {
        new [] {
            InlineKeyboardButton.WithCallbackData("✅ Корректирующий /a", "await_a"),
            InlineKeyboardButton.WithCallbackData("✏️ Новый запрос /c",  "await_c")
        }
    });
    }
}
