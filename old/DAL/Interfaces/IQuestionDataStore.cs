using DAL.Models;

namespace DAL.Interfaces
{
    public interface IQuestionDataStore
    {
        public Task<List<QuestionRecord>> GetQuestions();

        public void SaveOrUpdateQuestionsAsync(IEnumerable<QuestionRecord> questions,
                                             CancellationToken ct = default);

        public Task<QuestionRecord> GetQuestionByKey(DateOnly date, TimeOnly time, string seller);
        public QuestionRecord? GetNewestQuestionRecord();
    }
}
