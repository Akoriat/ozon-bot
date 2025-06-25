using System.ComponentModel.DataAnnotations;

namespace DAL.Models
{
    public class QuestionRecord
    {
        [Key]
        public int Id { get; set; }
        public DateOnly Date { get; set; }
        public TimeOnly Time { get; set; }
        public string ClientName { get; set; }
        public string Product { get; set; }
        public string Question { get; set; }
        public string Answers { get; set; }
        public string Usefulness { get; set; }
        public string ChatConversation { get; set; }
        public string Article { get; set; }
    }
}