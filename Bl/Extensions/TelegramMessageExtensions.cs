using Entities.DTOs;
using Telegram.Bot.Types;

namespace Bl.Extensions;
public static class TelegramMessageExtensions
{
    public static TelegramMessageDto ToDto(this Message src) => new TelegramMessageDto
    {
        ChatId = src.Chat.Id,
        ThreadId = src.MessageThreadId,
        MessageId = src.MessageId,
        UserId = src.From!.Id,
        Text = src.Text
    };
}
