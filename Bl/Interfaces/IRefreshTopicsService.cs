
using Bl.Common.DTOs;

namespace Bl.Interfaces
{
    public interface IRefreshTopicsService
    {
        public void RefreshTopics(RefreshTopicDto refreshTopicDto, CancellationToken cancellationToken);
    }
}
