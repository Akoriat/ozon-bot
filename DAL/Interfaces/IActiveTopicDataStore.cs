using DAL.Models;

namespace DAL.Interfaces
{
    public interface IActiveTopicDataStore
    {
        public Task<int> AddActiveTopicAsync(ActiveTopic activeTopic);

        public Task<List<ActiveTopic>> GetActiveTopicsAsync();

        public Task DeleteActiveTopicAsync(ActiveTopic activeTopic);

        public Task DeleteActiveTopicByMessageThreadIdAsync(string messageThreadId);

    }
}
