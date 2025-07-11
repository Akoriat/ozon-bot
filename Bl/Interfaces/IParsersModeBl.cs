using Common.Enums;
using DAL.Models;

namespace Bl.Interfaces
{
    public interface IParsersModeBl
    {
        Task<List<ParsersMode>> GetAllModesAsync(CancellationToken ct);
        Task<bool> ToggleModeAsync(ParserType parserName, CancellationToken ct);
    }
}
