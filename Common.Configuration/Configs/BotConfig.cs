namespace Common.Configuration.Configs; 

public class BotConfig
{
    public string Token { get; set; } = null!;
    public long ForumChatId { get; set; }
    public string ApiHash { get; set; }
    public string ApiId { get; set; }
    
}
