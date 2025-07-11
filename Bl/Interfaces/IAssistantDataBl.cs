using Entities;

namespace Bl.Interfaces;
public interface IAssistantDataBl
{
    public Task AddOrUpdateAsync(AssistantData assistantData);
    public Task<AssistantData?> GetByIdAsync(int id);
    public Task<AssistantData?> GetByAssistantIdAsync(string assistantId);
    public Task<List<AssistantData>> GetAllAsync();
    public Task DeleteAsync(int id);
}
