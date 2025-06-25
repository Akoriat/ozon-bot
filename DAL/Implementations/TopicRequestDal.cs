using DAL.Data;
using DAL.Interfaces;
using DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace DAL.Implementations
{
    public class TopicRequestDal : ITopicRequestDal
    {
        private readonly OzonBotDbContext _context;

        public TopicRequestDal(OzonBotDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(IList<TopicRequest> topicRequests)
        {
            await _context.TopicRequests.AddRangeAsync(topicRequests);
            await _context.SaveChangesAsync();
        }

        public async Task<IList<TopicRequest>> GetAllAsync()
        {
            return await _context.TopicRequests.ToListAsync();
        }

        public async Task<int> GetLastIdAsync()
        {
            return await _context.TopicRequests
                .OrderByDescending(x => x.Id)
                .Select(x => x.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<int> AddAsync(TopicRequest topicRequests)
        {
            await _context.TopicRequests.AddAsync(topicRequests);
            await _context.SaveChangesAsync();
            return topicRequests.Id;
        }

        public async Task<TopicRequest> GetByIdAsync(int id)
        {
            return await _context.TopicRequests
                .FirstOrDefaultAsync(x => x.Id == id);
        }
        public async Task UpdateAsync(TopicRequest topicRequest)
        {
            var topic = await _context.TopicRequests.FirstOrDefaultAsync(x => x.Id == topicRequest.Id);
            topic = topicRequest;
            await _context.SaveChangesAsync();
        }

        public async Task<TopicRequest?> TryGetTopicInfo(string requestId)
        {
            return await _context.TopicRequests.FirstOrDefaultAsync(t => t.RequestId == requestId);
        }

        public async Task<TopicRequest?> TryGetTopicInfoByThreadId(int threadId)
        {
            return await _context.TopicRequests.FirstOrDefaultAsync(t => t.MessageThreadId == threadId);
        }

        public async Task UpdateGptDraftAnswer(int id, string gptAnswer)
        {
            var topic = await _context.TopicRequests.FirstOrDefaultAsync(x => x.Id == id);
            topic.GptDraftAnswer = gptAnswer;
        }
    }
}
