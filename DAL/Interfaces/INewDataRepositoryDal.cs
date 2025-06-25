using DAL.Models;

namespace DAL.Interfaces
{
    public interface INewDataRepositoryDal
    {
        Task AddEntryAsync(NewDataEntry entry);
        void AddEntry(NewDataEntry entry);
        Task<IEnumerable<NewDataEntry>> GetUnprocessedEntriesAsync();
        Task MarkEntryAsProcessedAsync(int id);
        // Добавляем метод для поиска записи по SourceRecordId (уникальному ключу)
        Task<NewDataEntry?> GetEntryBySourceRecordIdAsync(string sourceRecordId);
        NewDataEntry? GetEntryBySourceRecordId(string sourceRecordId);
    }
}
