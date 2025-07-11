using Bl.Interfaces;
using DAL.Interfaces;
using Entities;

namespace Bl.Implementations;
public class AssistantDataBl : IAssistantDataBl
{
    private readonly IAssistantDataDal _assistantDataDal;
    public AssistantDataBl(IAssistantDataDal assistantDataDal)
    {
        _assistantDataDal = assistantDataDal;
    }
    public async Task AddOrUpdateAsync(AssistantData assistantData)
    {
        await _assistantDataDal.AddOrUpdateAsync(_assistantDataDal.FromEntityToDb(assistantData));
    }
    public async Task<AssistantData?> GetByIdAsync(int id)
    {
        return _assistantDataDal.FromDbToEntity(await _assistantDataDal.GetByIdAsync(id));
    }
    public async Task<AssistantData?> GetByAssistantIdAsync(string assistantId)
    {
        return _assistantDataDal.FromDbToEntity(await _assistantDataDal.GetByAssistantIdAsync(assistantId));
    }
    public async Task<List<AssistantData>> GetAllAsync()
    {
        var listModels = await _assistantDataDal.GetAllAsync();
        if (listModels == null || listModels.Count == 0)
        {
            return new List<AssistantData>();
        }
        return listModels
            .Where(model => model != null)
            .Select(_assistantDataDal.FromDbToEntity)
            .ToList()!;
    }
    public async Task DeleteAsync(int id)
    {
        await _assistantDataDal.DeleteAsync(id);
    }
}
