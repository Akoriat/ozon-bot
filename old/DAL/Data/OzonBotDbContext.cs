using Microsoft.EntityFrameworkCore;
using DAL.Models;

namespace DAL.Data
{
    public class OzonBotDbContext : DbContext
    {
        public OzonBotDbContext(DbContextOptions<OzonBotDbContext> options)
            : base(options)
        { }

        public DbSet<ChatRecord> ChatRecords { get; set; } = null!;
        public DbSet<NewDataEntry> NewDataEntries { get; set; } = null!;
        public DbSet<QuestionRecord> QuestionRecords { get; set; } = null!;
        public DbSet<Review> Reviews { get; set; } = null!;
        public DbSet<TopicRequest> TopicRequests { get; set; } = null!;
        public DbSet<CorrectAnswer> CorrectAnswers { get; set; } = null!;
        public DbSet<AssistantMode> AssistantModes { get; set; } = null!;
        public DbSet<ActiveTopic> ActiveTopics { get; set; } = null;
        public DbSet<ParsersMode> ParsersModes { get; set; } = null!;
    }
}
