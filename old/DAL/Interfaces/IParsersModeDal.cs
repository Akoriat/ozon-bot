using DAL.Models;

namespace DAL.Interfaces
{
    public interface IParsersModeDal
    {
        Task<List<ParsersMode>> GetAllModesAsync(CancellationToken ct);
        Task<bool> ToggleModeAsync(string parserName, CancellationToken ct);
    }
}
