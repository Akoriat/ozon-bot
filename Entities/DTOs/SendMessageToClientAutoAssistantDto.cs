namespace Entities.DTOs;

public class SendMessageToClientAutoAssistantDto
{
    public string RequestId { get; set; }
    public string ParserName { get; set; }
    public string GptDraftAnswer { get; set; }
}
