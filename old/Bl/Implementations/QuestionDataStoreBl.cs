using Bl.Interfaces;
using DAL.Interfaces;
using DAL.Models;

namespace Bl.Implementations
{
    public class QuestionDataStoreBl : IQuestionDataStoreBl
    {
        protected readonly IQuestionDataStore _questionDataStore;
        public QuestionDataStoreBl(IQuestionDataStore questionDataStore)
        {
            _questionDataStore = questionDataStore;
        }
        public async Task<QuestionRecord> GetQuestionByKey(string key)
        {
            var temp = key.Split('_');
            var date = DateOnly.ParseExact(temp[1], "dd.MM.yyyy");
            var time = TimeOnly.Parse(temp[2]);
            var seller = temp[3];
            return await _questionDataStore.GetQuestionByKey(date, time, seller);
        }
        public async Task<List<QuestionRecord>> GetQuestions()
        {
            return await _questionDataStore.GetQuestions();
        }

        public void SaveOrUpdateQuestions(IEnumerable<QuestionRecord> questions,
                                             CancellationToken ct = default)
        {
            _questionDataStore.SaveOrUpdateQuestionsAsync(questions, ct);
        }

        public QuestionRecord? GetNewestQuestionRecord()
        {
            return _questionDataStore.GetNewestQuestionRecord();
        }

    }
}
