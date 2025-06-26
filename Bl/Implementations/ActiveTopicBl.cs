using Bl.Interfaces;
using DAL.Interfaces;
using DAL.Models;

namespace Bl.Implementations
{
    public class ActiveTopicBl : IActiveTopicBl
    {
        private readonly IActiveTopicDataStore _activeTopicDataStore;
        public ActiveTopicBl(IActiveTopicDataStore activeTopicDataStore)
        {
            _activeTopicDataStore = activeTopicDataStore;
        }
        public async Task<int> AddActiveTopicAsync(ActiveTopic activeTopic)
        {
            var existing = (await _activeTopicDataStore.GetActiveTopicsAsync())
                .FirstOrDefault(x => x.RequestId == activeTopic.RequestId
                                   || x.MessageThreadId == activeTopic.MessageThreadId);
            if (existing != null)
                return existing.Id;

            return await _activeTopicDataStore.AddActiveTopicAsync(activeTopic);
        }

        public async Task<List<ActiveTopic>> GetActiveTopicsAsync()
        {
            return await _activeTopicDataStore.GetActiveTopicsAsync();
        }

        public async Task DeleteActiveTopicAsync(ActiveTopic activeTopic)
        {
            await _activeTopicDataStore.DeleteActiveTopicAsync(activeTopic);
        }

        public async Task DeleteActiveTopicByMessageThreadIdAsync(string messageThreadId)
        {
            await _activeTopicDataStore.DeleteActiveTopicByMessageThreadIdAsync(messageThreadId);
        }
    }
}
