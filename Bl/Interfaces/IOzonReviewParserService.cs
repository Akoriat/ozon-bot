using Bl.Common.DTOs;
using DAL.Models;

namespace Bl.Interfaces
{
    public interface IOzonReviewParserService
    {
        //public List<InTopicModelDto<Review>> ParseIteration(DateOnly date, TimeOnly time, out bool hasMore);
        public Task<(List<InTopicModelDto<Review>>, bool)> ParseIteration(DateOnly date, TimeOnly time, CancellationToken ct);
        public void ClickButton(out bool hasMore);
        public void Navigate(string url, int maxAttempts = 3, int waitSeconds = 30);
        bool SendMessageToClient(string requestId, string message, string article);
        public bool RefreshActiveTopic(string requestId, string article);
        public void ProcessedButtonClick();
        public Task<bool> ClickCalendarDate(DateOnly date);
    }
}
