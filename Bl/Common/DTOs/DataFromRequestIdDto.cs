namespace Bl.Common.DTOs
{
    public class DataFromRequestIdDto
    {
        public DateOnly FindDate { get; set; }
        public TimeOnly? FindTime { get; set; }
        public string? FindClientName { get; set; }
    }
}
