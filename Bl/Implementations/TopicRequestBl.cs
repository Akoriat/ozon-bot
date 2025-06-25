using Bl.Interfaces;
using DAL.Interfaces;
using DAL.Models;

namespace Bl.Implementations
{
    public class TopicRequestBl : ITopicRequestBl
    {
        private readonly ITopicRequestDal _topicRequestDal;
        public TopicRequestBl(ITopicRequestDal topicRequestDal)
        {
            _topicRequestDal = topicRequestDal;
        }
        public async Task AddAsync(IList<TopicRequest> topicRequests)
        {
            await _topicRequestDal.AddAsync(topicRequests);
        }

        public async Task<IList<TopicRequest>> GetAllAsync()
        {
            return await _topicRequestDal.GetAllAsync();
        }

        public async Task<int> GetLastIdAsync()
        {
            return await _topicRequestDal.GetLastIdAsync();
        }

        public async Task<int> AddAsync(TopicRequest topicRequests)
        {
            return await _topicRequestDal.AddAsync(topicRequests);
        }

        public async Task<TopicRequest> GetByIdAsync(int id)
        {
            return await _topicRequestDal.GetByIdAsync(id);
        }

        public async Task UpdateAsync(TopicRequest topicRequest)
        {
            await _topicRequestDal.UpdateAsync(topicRequest);
        }

        public async Task<TopicRequest?> TryGetTopicInfo(string requestId)
        {
            return await _topicRequestDal.TryGetTopicInfo(requestId);
        }

        public async Task<TopicRequest?> TryGetTopicInfoByThreadId(int threadId)
        {
            return await _topicRequestDal.TryGetTopicInfoByThreadId(threadId);
        }

        public async Task UpdateGptDraftAnswer(int id, string gptAnswer)
        {
            await _topicRequestDal.UpdateGptDraftAnswer(id, gptAnswer);
        }
    }
}
