using DAL.Models;

namespace Bl.Interfaces
{
    public interface IQuestionDataStoreBl
    {
        public void SaveOrUpdateQuestions(IEnumerable<QuestionRecord> questions,
                                             CancellationToken ct = default);
        public Task<List<QuestionRecord>> GetQuestions();
        Task<QuestionRecord> GetQuestionByKey(string key);
        public QuestionRecord? GetNewestQuestionRecord();
    }
}
