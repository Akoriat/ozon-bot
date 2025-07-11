using Bl.Interfaces;
using DAL.Interfaces;
using DAL.Models;

namespace Bl.Implementations;

public class AssistantModeBl : IAssistantModeBl
{
    private readonly IAssistantModeDal _assistantModeDal;
    public AssistantModeBl(IAssistantModeDal assistantModeDal)
    {
        _assistantModeDal = assistantModeDal;
    }

    public async Task<List<AssistantMode>> GetAllModesAsync(CancellationToken ct)
    {
        return await _assistantModeDal.GetAllModesAsync(ct);
    }

    public async Task<bool> ToggleModeAsync(string assistantName, CancellationToken ct)
    {
        return await _assistantModeDal.ToggleModeAsync(assistantName, ct);
    }
}
