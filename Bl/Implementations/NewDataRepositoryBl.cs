using Bl.Interfaces;
using DAL.Interfaces;
using DAL.Models;

namespace Bl.Implementations
{
    public class NewDataRepositoryBl : INewDataRepositoryBl
    {
        protected readonly INewDataRepositoryDal _newDataRepositoryDal;
        public NewDataRepositoryBl(INewDataRepositoryDal newDataRepositoryDal) 
        {
            _newDataRepositoryDal = newDataRepositoryDal;
        }

        public void AddEntry(NewDataEntry entry)
        {
            _newDataRepositoryDal.AddEntry(entry);
        }

        public Task AddEntryAsync(NewDataEntry entry)
        {
            return _newDataRepositoryDal.AddEntryAsync(entry);
        }

        public NewDataEntry? GetEntryBySourceRecordId(string sourceRecordId)
        {
            return _newDataRepositoryDal.GetEntryBySourceRecordId(sourceRecordId);
        }

        public Task<NewDataEntry?> GetEntryBySourceRecordIdAsync(string sourceRecordId)
        {
            return _newDataRepositoryDal.GetEntryBySourceRecordIdAsync(sourceRecordId);
        }

        public Task<IEnumerable<NewDataEntry>> GetUnprocessedEntriesAsync()
        {
            return _newDataRepositoryDal.GetUnprocessedEntriesAsync();
        }

        public Task MarkEntryAsProcessedAsync(int id)
        {
            return _newDataRepositoryDal.MarkEntryAsProcessedAsync(id);
        }
    }
}
