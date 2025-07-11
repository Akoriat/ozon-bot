namespace Common.Configuration.Configs;

public class ChatGptConfig
{
    public string ApiKey { get; set; }
    public Dictionary<string, string> Assistants { get; set; } = new Dictionary<string, string>();
}
