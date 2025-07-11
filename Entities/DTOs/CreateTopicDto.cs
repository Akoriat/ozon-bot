using Common.Enums;

namespace Entities.DTOs;

public class CreateTopicDto
{
    public string RequestId { get; set; }
    public string UserQuestion { get; set; }
    public string ParserName { get; set; }
    public string ForChatGpt { get; set; }
    public string ClientName { get; set; }
    public string? Product { get; set; }
    public AssistantType AssistantType { get; set; }
    public string? Article { get; set; }
    public int? Rating { get; set; }
    public string? Photo { get; set; }
    public string? Video { get; set; }
    public string? TopicName { get; set; }
    public string? FullChat { get; set; } = null;
}
