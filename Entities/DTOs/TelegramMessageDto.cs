namespace Entities.DTOs;
public sealed class TelegramMessageDto
{
    public long ChatId { get; set; }
    public int? ThreadId { get; set; }
    public int MessageId { get; set; }
    public long UserId { get; set; }
    public string? Text { get; set; }
}
