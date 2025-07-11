namespace Entities.DTOs;

public class RefreshTopicDto
{
    public IEnumerable<string> RequestIdsForQuestion { get; set; }
    public IEnumerable<string> RequestIdsForReviews { get; set; }
}
