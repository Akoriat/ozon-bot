using DAL.Data;
using DAL.Interfaces;
using DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace DAL.Implementations
{
    public class ParserDateLimitDal : IParserDateLimitDal
    {
        private readonly OzonBotDbContext _context;
        public ParserDateLimitDal(OzonBotDbContext context)
        {
            _context = context;
        }

        public async Task<List<ParserDateLimit>> GetAllAsync(CancellationToken ct)
        {
            return await _context.ParserDateLimits.ToListAsync(ct);
        }

        public async Task<DateOnly?> GetStopDateAsync(string parserName, CancellationToken ct)
        {
            var entity = await _context.ParserDateLimits.FirstOrDefaultAsync(x => x.ParserName == parserName, ct);
            return entity?.StopDate;
        }

        public async Task SetStopDateAsync(string parserName, DateOnly date, CancellationToken ct)
        {
            var entity = await _context.ParserDateLimits.FirstOrDefaultAsync(x => x.ParserName == parserName, ct);
            if (entity == null)
            {
                entity = new ParserDateLimit { ParserName = parserName, StopDate = date };
                _context.ParserDateLimits.Add(entity);
            }
            else
            {
                entity.StopDate = date;
            }
            await _context.SaveChangesAsync(ct);
        }
    }
}
