using Common.Enums;
using Entities.DTOs;

namespace Bl.Interfaces;

public interface ILastMessageIdFromGeneralBl
{
    public Task AddOrUpdateAsync(LastMessageDto lastMessageIdFromGeneral);
    public Task<LastMessageType?> GetLastMessageTypeAsync();
}
