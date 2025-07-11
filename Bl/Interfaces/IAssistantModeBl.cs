using Common.Enums;
using DAL.Models;

namespace Bl.Interfaces
{
    public interface IAssistantModeBl
    {
        /// <summary>Возвращает все ассистенты и их режимы (true = авто, false = ручной)</summary>
        Task<List<AssistantMode>> GetAllModesAsync(CancellationToken ct);
        /// <summary>Переключает режим конкретного ассистента и возвращает новый режим</summary>
        Task<bool> ToggleModeAsync(AssistantType assistantType, CancellationToken ct);
    }

}
