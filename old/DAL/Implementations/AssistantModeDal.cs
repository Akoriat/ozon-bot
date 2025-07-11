using DAL.Data;
using DAL.Interfaces;
using DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace DAL.Implementations
{
    public class AssistantModeDal : IAssistantModeDal
    {
        private readonly OzonBotDbContext _context;
        public AssistantModeDal(OzonBotDbContext context)
        {
            _context = context;
        }

        public async Task<List<AssistantMode>> GetAllModesAsync(CancellationToken ct)
        {
            return await _context.AssistantModes.ToListAsync(ct);
        }

        public async Task<bool> ToggleModeAsync(string assistantName, CancellationToken ct)
        {
            var model = await _context.AssistantModes.FirstOrDefaultAsync(x => x.AssistantName == assistantName, ct);
            if (model is null) 
            {
                return false;
            }
            model.IsAuto = !model.IsAuto;
            await _context.SaveChangesAsync(ct);
            return true;
        }
    }
}
