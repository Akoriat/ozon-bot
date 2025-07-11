using Common.Enums;
using DAL.Data;
using DAL.Interfaces;
using DAL.Models;
using Microsoft.EntityFrameworkCore;

namespace DAL.Implementations;

public class LastMessageIdFromGeneralDal : ILastMessageIdFromGeneralDal
{
    private readonly OzonBotDbContext _dbContext;
    public LastMessageIdFromGeneralDal(OzonBotDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    public async Task AddOrUpdateAsync(LastMessageIdFromGeneral lastMessageIdFromGeneral)
    {
        var exist = await _dbContext.LastMessageIdFromGenerals.FirstOrDefaultAsync(x => x.LastMessageId == lastMessageIdFromGeneral.LastMessageId
            && x.LastMessageType == lastMessageIdFromGeneral.LastMessageType);

        if (exist == null)
        {
            await _dbContext.LastMessageIdFromGenerals.AddAsync(lastMessageIdFromGeneral);
            await _dbContext.SaveChangesAsync();
            return;
        }
    }

    public async Task<LastMessageType?> GetLastMessageTypeAsync()
    {
        return await _dbContext.LastMessageIdFromGenerals
            .OrderByDescending(x => x.Id)
            .Select(x => (LastMessageType)x.LastMessageType)
            .FirstOrDefaultAsync();
    }
}
