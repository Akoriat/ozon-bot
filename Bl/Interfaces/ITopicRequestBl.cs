﻿using DAL.Models;

namespace Bl.Interfaces
{
    public interface ITopicRequestBl
    {
        public Task<IList<TopicRequest>> GetAllAsync();
        public Task<int> GetLastIdAsync();
        public Task AddAsync(IList<TopicRequest> topicRequests);
        public Task<int> AddAsync(TopicRequest topicRequests);
        public Task<TopicRequest> GetByIdAsync(int id);
        public Task UpdateAsync(TopicRequest topicRequest);
        public Task<TopicRequest?> TryGetTopicInfo(string requestId);
        public Task<TopicRequest?> TryGetTopicInfoByThreadId(int threadId);
        public Task UpdateGptDraftAnswer(int id, string gptAnswer);
    }
}
