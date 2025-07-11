using DAL.Models;

namespace Bl.Interfaces
{
    public interface IChatParserBl
    {
        public List<ChatRecord> LoadChats();
        public void AddOrUpdateChats(List<ChatRecord> data);
        public ChatRecord? GetNewestChatDate();
        public ChatRecord GetChatRecordByChatId(string chatId);
        public HashSet<string> GetLatestChatIds();
    }
}
