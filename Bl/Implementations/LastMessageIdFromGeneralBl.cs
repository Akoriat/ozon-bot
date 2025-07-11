using Bl.Interfaces;
using Common.Enums;
using DAL.Interfaces;
using Entities.DTOs;

namespace Bl.Implementations;

public class LastMessageIdFromGeneralBl : ILastMessageIdFromGeneralBl
{
    private readonly ILastMessageIdFromGeneralDal _lastMessageIdFromGeneralDal;
    public LastMessageIdFromGeneralBl(ILastMessageIdFromGeneralDal lastMessageIdFromGeneralDal)
    {
        _lastMessageIdFromGeneralDal = lastMessageIdFromGeneralDal;
    }
    public async Task AddOrUpdateAsync(LastMessageDto lastMessageIdFromGeneral)
    {
        await _lastMessageIdFromGeneralDal.AddOrUpdateAsync(new DAL.Models.LastMessageIdFromGeneral 
            { LastMessageId = lastMessageIdFromGeneral.LastMessageId
            , LastMessageType = (int)lastMessageIdFromGeneral.LastMessageType });
    }

    public Task<LastMessageType?> GetLastMessageTypeAsync()
    {
        return _lastMessageIdFromGeneralDal.GetLastMessageTypeAsync();
    }
}
