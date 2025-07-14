namespace Common.Configuration.Configs;

public class ChatGptConfig
{
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>
    /// Ассистенты из конфига (имя -> id). Можно добавлять новые без перекомпиляции.
    /// </summary>
    public Dictionary<string, string> Assistants { get; set; } = new();
}
