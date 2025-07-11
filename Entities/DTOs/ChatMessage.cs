using Newtonsoft.Json;

namespace Entities.DTOs;

public class ChatMessage
{
    [JsonProperty("role")]
    public string Role { get; set; }
    [JsonProperty("content")]
    public string Content { get; set; }
}
