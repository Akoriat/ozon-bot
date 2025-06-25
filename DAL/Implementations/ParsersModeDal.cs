using DAL.Data;
using DAL.Interfaces;
using DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace DAL.Implementations
{
    public class ParsersModeDal : IParsersModeDal
    {
        private readonly OzonBotDbContext _dbContext;
        public ParsersModeDal(OzonBotDbContext dbContext)
        {
            _dbContext = dbContext;
        }
        public async Task<List<ParsersMode>> GetAllModesAsync(CancellationToken ct)
        {
            return await _dbContext.ParsersModes.ToListAsync(ct);
        }

        public async Task<bool> ToggleModeAsync(string parserName, CancellationToken ct)
        {
            var model = await _dbContext.ParsersModes.FirstOrDefaultAsync(x => x.ParserName == parserName, ct);
            if (model is null)
            {
                return false;
            }
            model.IsActive = !model.IsActive;
            await _dbContext.SaveChangesAsync(ct);
            return true;
        }
    }
}
