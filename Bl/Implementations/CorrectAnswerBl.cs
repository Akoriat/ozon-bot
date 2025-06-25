using Bl.Interfaces;
using DAL.Interfaces;
using DAL.Models;

namespace Bl.Implementations
{
    public class CorrectAnswerBl : ICorrectAnswerBl
    {
        private readonly ICorrectAnswerDbDataStore _correctAnswerDbDataStore;
        public CorrectAnswerBl(ICorrectAnswerDbDataStore correctAnswerDbDataStore)
        {
            _correctAnswerDbDataStore = correctAnswerDbDataStore;
        }
        public async Task AddAsync(CorrectAnswer correctAnswer)
        {
            await _correctAnswerDbDataStore.AddAsync(correctAnswer);
        }
    }
}
