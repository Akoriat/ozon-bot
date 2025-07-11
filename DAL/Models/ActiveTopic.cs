namespace DAL.Models;

public class ActiveTopic
{
    public int Id { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public string MessageThreadId { get; set; } = string.Empty;
    public string ParserName { get; set; } = string.Empty;
    public string AssistantType { get; set; } = string.Empty;
    public string? Article { get; set; }
}
