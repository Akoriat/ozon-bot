namespace Entities;
public class AssistantData
{
    public int Id { get; set; } = 0;
    public string AssistantName { get; set; } = string.Empty;
    public string AssistantId { get; set; } = string.Empty;

    public AssistantData(string assistantName, string assistantId) 
    {
        AssistantName = assistantName;
        AssistantId = assistantId;
    }
}
