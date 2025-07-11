
namespace DAL.Models
{
    public class TopicRequest
    {
        public int Id { get; set; }
        public string RequestId { get; set; } = "";
        public int MessageThreadId { get; set; }
        public string UserQuestion { get; set; } = "";
        public string GptDraftAnswer { get; set; } = "";
        public string Status { get; set; } = null!;
        public string ParserName { get; set; } = "Unknown"!;
        public int AssistantType { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
