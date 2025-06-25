namespace Bl.Interfaces
{
    public interface IChatParserService
    {
        void Navigate(string url, int maxAttempts = 3, int waitSeconds = 30);
        /// <summary>
        /// Полный парсинг всех чатов.
        /// </summary>
        HashSet<string> ExtractAllChats();

        /// <summary>
        /// Инкрементальный парсинг, где в качестве существующих данных передаётся ранее сохранённая коллекция.
        /// </summary>
        HashSet<string> ExtractNewChats(HashSet<string> chatIds, DateOnly? minDate = null);

        /// <summary>
        /// Обновление диалогов (если появились новые сообщения) для уже сохранённых чатов.
        /// </summary>
        HashSet<string> UpdateChats(DateOnly? minDate = null);
        bool SendMessageToClient(string requestId, string message);

        public bool RefreshActiveTopic(string requestId);
    }
}
