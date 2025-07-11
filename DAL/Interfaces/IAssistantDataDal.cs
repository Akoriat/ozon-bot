using DAL.Models;

namespace DAL.Interfaces;
public interface IAssistantDataDal
{
    public Task AddOrUpdateAsync(AssistantData? assistantData);
    public Task<AssistantData?> GetByIdAsync(int id);
    public Task<AssistantData?> GetByAssistantIdAsync(string assistantId);
    public Task<List<AssistantData>> GetAllAsync();
    public Task DeleteAsync(int id);
    public AssistantData? FromEntityToDb(Entities.AssistantData? entity);
    public Entities.AssistantData? FromDbToEntity(AssistantData? dbObject);
}
