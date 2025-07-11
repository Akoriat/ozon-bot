using Common.Enums;
using DAL.Models;

namespace DAL.Interfaces;

public interface ILastMessageIdFromGeneralDal
{
    Task AddOrUpdateAsync(LastMessageIdFromGeneral lastMessageIdFromGeneral);
    Task<LastMessageType?> GetLastMessageTypeAsync();
}
