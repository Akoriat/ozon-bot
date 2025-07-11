using Common.Enums;

namespace Bl.Interfaces
{
    public interface IChatGPTClient
    {
        public Task<string> SendMessageAsync(string userMessage, AssistantType assistantType, CancellationToken ct = default);
    }
}
