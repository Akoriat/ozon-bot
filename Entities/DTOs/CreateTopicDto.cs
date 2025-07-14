namespace Entities.DTOs;

public class CreateTopicDto
{
    public string RequestId { get; set; } = string.Empty;
    public string UserQuestion { get; set; } = string.Empty;
    public string ParserName { get; set; } = string.Empty;
    public string ForChatGpt { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string? Product { get; set; }
    public string AssistantType { get; set; } = string.Empty;
    public string? Article { get; set; }
    public int? Rating { get; set; }
    public string? Photo { get; set; }
    public string? Video { get; set; }
    public string? TopicName { get; set; }
    public string? FullChat { get; set; } = null;
}
