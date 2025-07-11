using DAL.Models;

namespace DAL.Interfaces
{
    public interface ICorrectAnswerDbDataStore
    {
        Task AddAsync(CorrectAnswer correctAnswer);
    }
}
