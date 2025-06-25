using Bl.Common.DTOs;

namespace Bl.Interfaces
{
    public interface IForumTopicService
    {
        public Task CreateTopicForRequestAsync(CreateTopicDto createTopicDto, CancellationToken ct);
    }
}
