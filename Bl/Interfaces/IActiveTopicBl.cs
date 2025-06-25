using DAL.Models;

namespace Bl.Interfaces
{
    public interface IActiveTopicBl
    {
        public Task<int> AddActiveTopicAsync(ActiveTopic activeTopic);

        public Task<List<ActiveTopic>> GetActiveTopicsAsync();

        public Task DeleteActiveTopicAsync(ActiveTopic activeTopic);
        public Task DeleteActiveTopicByMessageThreadIdAsync(string messageThreadId);
    }
}
