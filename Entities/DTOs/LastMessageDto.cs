using Common.Enums;

namespace Entities.DTOs;

public class LastMessageDto
{
    public int LastMessageId { get; set; }
    public LastMessageType LastMessageType { get; set; }
}
