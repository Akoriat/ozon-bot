using DAL.Data;
using DAL.Interfaces;
using DAL.Models;

namespace DAL.Implementations
{
    public class CorrectAnswerDbDataStore : ICorrectAnswerDbDataStore
    {
        private readonly OzonBotDbContext _dbContext;
        public CorrectAnswerDbDataStore(OzonBotDbContext dbContext)
        {
            _dbContext = dbContext;
        }
        public async Task AddAsync(CorrectAnswer correctAnswer)
        {
            await _dbContext.CorrectAnswers.AddAsync(correctAnswer);
        }
    }
}
