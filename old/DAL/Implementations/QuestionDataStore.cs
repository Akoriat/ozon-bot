using DAL.Models;
using DAL.Interfaces;
using Microsoft.EntityFrameworkCore;
using DAL.Data;

namespace DAL.Implementations
{
    public class QuestionDataStore : IQuestionDataStore
    {
        private readonly OzonBotDbContext _dbContext;
        public QuestionDataStore(OzonBotDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<QuestionRecord>> GetQuestions()
        {
            var records = await _dbContext.QuestionRecords.OrderBy(r => r.Id).ToListAsync();
            return records;
        }

        public void SaveOrUpdateQuestionsAsync(IEnumerable<QuestionRecord> questions,
                                             CancellationToken ct = default)
        {
            var ids = questions.Select(q => q.Id).ToList();

            var dbQuestions = _dbContext.QuestionRecords
                                              .Where(q => ids.Contains(q.Id))
                                              .ToDictionary(q => q.Id);

            foreach (var incoming in questions)
            {
                if (dbQuestions.TryGetValue(incoming.Id, out var tracked))
                {
                    _dbContext.Entry(tracked).CurrentValues.SetValues(incoming);
                }
                else
                {
                    _dbContext.QuestionRecords.Add(incoming);
                }
            }

            _dbContext.SaveChanges();
        }

        public async Task<QuestionRecord> GetQuestionByKey(DateOnly date, TimeOnly time, string seller)
        {
            return await _dbContext.QuestionRecords.FirstOrDefaultAsync(x => x.Time == time && x.Date == date && x.ClientName == seller);
        }

        public QuestionRecord? GetNewestQuestionRecord()
        {
            return _dbContext.QuestionRecords.OrderByDescending(x => x.Date).ThenByDescending(x => x.Time).FirstOrDefault();
        }
    }
}
