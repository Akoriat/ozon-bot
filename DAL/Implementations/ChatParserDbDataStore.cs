using DAL.Data;
using DAL.Interfaces;
using DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace DAL.Implementations
{
    public class ChatParserDbDataStore : IChatParserDataStore
    {
        private readonly OzonBotDbContext _dbContext;

        public ChatParserDbDataStore(OzonBotDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public ChatRecord GetChatRecordByChatId(string chatId)
        {
            var chatRecord = _dbContext.ChatRecords.FirstOrDefault(x => x.ChatId == chatId);
            return chatRecord;
        }

        public List<ChatRecord> LoadChats()
        {
            var chats = _dbContext.ChatRecords.OrderBy(x => x.Id).ToList();
            return chats;
        }

        public void AddOrUpdateChats(List<ChatRecord> data)
        {
            foreach (var chat in data)
            {
                var existingReview = _dbContext.ChatRecords
                    .FirstOrDefault(r => r.ChatId == chat.ChatId);

                if (existingReview == null)
                {
                    _dbContext.ChatRecords.Add(chat);
                }
                else
                {
                    existingReview.History = string.IsNullOrEmpty(chat.History) ? existingReview.History : chat.History;
                    existingReview.Preview = string.IsNullOrEmpty(chat.Preview) ? existingReview.Preview : chat.Preview;
                    existingReview.Date = chat.Date == null ? existingReview.Date : chat.Date;
                    existingReview.Unread = string.IsNullOrEmpty(chat.Unread) ? existingReview.Unread : chat.Unread;
                    existingReview.Title = string.IsNullOrEmpty(chat.Title) ? existingReview.Title : chat.Title;
                }
            }

            _dbContext.SaveChanges();
        }

        public ChatRecord? GetNewestChatDate()
        {
            var lastChat = _dbContext.ChatRecords
                .OrderByDescending(x => x.Date)
                .FirstOrDefault();

            return lastChat;
        }

        public HashSet<string> GetLatestChatIds()
        {
            var latestChatIds = _dbContext.ChatRecords.OrderByDescending(x => x.Date).Select(x => x.ChatId).Take(100).ToHashSet();
            return latestChatIds ?? new HashSet<string>();
        }
    }
}
