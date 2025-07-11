using Bl.Common.DTOs;
using DAL.Models;

namespace Bl.Interfaces
{
    public interface IAnswerClientService
    {
        public bool SendMessageToClientManualAssistant(TopicRequest topic, string? adminMessage = null);
        public bool SendMessageToClientAutoAssistant(SendMessageToClientAutoAssistantDto topic);
        public bool RefreshTopic(ActiveTopic activeTopic);
    }
}
