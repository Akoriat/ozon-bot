namespace DAL.Models;

public class TopicRequest
{
    public int Id { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public int MessageThreadId { get; set; }
    public string UserQuestion { get; set; } = string.Empty;
    public string GptDraftAnswer { get; set; } = string.Empty;
    public string Status { get; set; } = null!;
    public string ParserName { get; set; } = "Unknown"!;
    public string AssistantType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
