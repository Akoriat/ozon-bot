using DAL.Models;

namespace Bl.Interfaces
{
    public interface ICorrectAnswerBl
    {
        Task AddAsync(CorrectAnswer correctAnswer);
    }
}
