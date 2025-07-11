using DAL.Data;
using DAL.Interfaces;
using DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace DAL.Implementations
{
    public class NewDataRepositoryDal : INewDataRepositoryDal
    {
        private readonly OzonBotDbContext _dbContext;
        public NewDataRepositoryDal(OzonBotDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task AddEntryAsync(NewDataEntry entry)
        {
            await _dbContext.NewDataEntries.AddAsync(entry);
            await _dbContext.SaveChangesAsync();
        }

        public async Task<IEnumerable<NewDataEntry>> GetUnprocessedEntriesAsync()
        {
            return await _dbContext.NewDataEntries
                .Where(e => !e.Processed)
                .ToListAsync();
        }

        public async Task MarkEntryAsProcessedAsync(int id)
        {
            var entry = await _dbContext.NewDataEntries.FirstOrDefaultAsync(e => e.Id == id);
            if (entry != null)
            {
                entry.Processed = true;
                entry.ProcessedAt = System.DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }
        }

        public async Task<NewDataEntry?> GetEntryBySourceRecordIdAsync(string sourceRecordId)
        {
            return await _dbContext.NewDataEntries
                .FirstOrDefaultAsync(e => e.SourceRecordId == sourceRecordId);
        }

        public void AddEntry(NewDataEntry entry)
        {
            _dbContext.NewDataEntries.Add(entry);
            _dbContext.SaveChanges();
        }

        public NewDataEntry? GetEntryBySourceRecordId(string sourceRecordId)
        {
            return _dbContext.NewDataEntries
                .FirstOrDefault(e => e.SourceRecordId == sourceRecordId);
        }
    }
}
