using DAL.Models;

namespace DAL.Interfaces
{
    public interface IChatParserDataStore
    {
        /// <summary>
        /// Загружает сохранённые данные из таблицы БД.
        /// Формат: первая строка – заголовки, далее – данные чатов.
        /// </summary>
        List<ChatRecord> LoadChats();

        /// <summary>
        /// Сохраняет данные в таблицу БД.
        /// </summary>
        void AddOrUpdateChats(List<ChatRecord> data);
        ChatRecord? GetNewestChatDate();

        ChatRecord GetChatRecordByChatId(string chatId);
        HashSet<string> GetLatestChatIds();
    }
}
