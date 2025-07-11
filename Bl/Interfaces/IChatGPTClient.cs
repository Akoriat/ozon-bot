namespace Bl.Interfaces;

public interface IChatGPTClient
{
    Task<string> SendMessageAsync(string userMessage, string assistantType, CancellationToken ct = default);
}
