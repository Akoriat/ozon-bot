using Bl.Interfaces;
using DAL.Interfaces;
using DAL.Models;

namespace Bl.Implementations
{
    public class ChatParserBl : IChatParserBl
    {
        protected readonly IChatParserDataStore _chatParserDataStore;
        public ChatParserBl(IChatParserDataStore chatParserDataStore)
        {
            _chatParserDataStore = chatParserDataStore;
        }
        public List<ChatRecord> LoadChats()
        {
            return _chatParserDataStore.LoadChats();
        }

        public ChatRecord? GetNewestChatDate()
        {
            return _chatParserDataStore.GetNewestChatDate();
        }

        public void AddOrUpdateChats(List<ChatRecord> data)
        {
            _chatParserDataStore.AddOrUpdateChats(data);
        }

        public ChatRecord GetChatRecordByChatId(string chatId)
        {
            return _chatParserDataStore.GetChatRecordByChatId(chatId);
        }

        public HashSet<string> GetLatestChatIds()
        {
            return _chatParserDataStore.GetLatestChatIds();
        }
    }
}
