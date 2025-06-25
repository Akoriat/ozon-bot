namespace DAL.Models
{
    public class ParserDateLimit
    {
        public int Id { get; set; }
        public string ParserName { get; set; } = "";
        public DateOnly StopDate { get; set; }
    }
}
