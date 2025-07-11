namespace DAL.Models
{
    public class CorrectAnswer
    {
        public int Id { get; set; }
        public string GptAnswer { get; set; }
        public string AdminAnswer { get; set; }
        public string UserMessage { get; set; }
    }
}