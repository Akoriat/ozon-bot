using DAL.Data;
using DAL.Interfaces;
using DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace DAL.Implementations
{
    public class ActiveTopicDataStore : IActiveTopicDataStore
    {
        private readonly OzonBotDbContext _db;
        public ActiveTopicDataStore(OzonBotDbContext ozonBotDbContext)
        {
            _db = ozonBotDbContext;
        }

        public async Task<int> AddActiveTopicAsync(ActiveTopic activeTopic)
        {
            await _db.ActiveTopics.AddAsync(activeTopic);
            await _db.SaveChangesAsync();
            return activeTopic.Id;
        }

        public async Task<List<ActiveTopic>> GetActiveTopicsAsync()
        {
            return await _db.ActiveTopics.ToListAsync();
        }

        public async Task DeleteActiveTopicAsync(ActiveTopic activeTopic)
        {
            _db.ActiveTopics.Remove(activeTopic);
            await _db.SaveChangesAsync();
        }

        public async Task DeleteActiveTopicByMessageThreadIdAsync(string messageThreadId)
        {
            var tmp = await _db.ActiveTopics.Where(x => x.MessageThreadId == messageThreadId).FirstOrDefaultAsync();
            _db.ActiveTopics.Remove(tmp);
            await _db.SaveChangesAsync();
        }
    }
}
