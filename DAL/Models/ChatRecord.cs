
namespace DAL.Models
{
    public class ChatRecord
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Unread { get; set; } = "0";
        public DateOnly Date { get; set; }
        public string ChatId { get; set; }
        public string Preview { get; set; }
        public string History { get; set; }
    }
}
