using DAL.Models;

namespace DAL.Interfaces
{
    public interface IAssistantModeDal
    {
        /// <summary>Возвращает все ассистенты и их режимы (true = авто, false = ручной)</summary>
        Task<List<AssistantMode>> GetAllModesAsync(CancellationToken ct);
        /// <summary>Переключает режим конкретного ассистента и возвращает новый режим</summary>
        Task<bool> ToggleModeAsync(string assistantName, CancellationToken ct);
    }
}
