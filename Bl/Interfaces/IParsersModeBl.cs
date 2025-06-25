using Bl.Common.Enum;
using DAL.Models;
using Telegram.Bot.Types.Enums;

namespace Bl.Interfaces
{
    public interface IParsersModeBl
    {
        Task<List<ParsersMode>> GetAllModesAsync(CancellationToken ct);
        Task<bool> ToggleModeAsync(ParserType parserName, CancellationToken ct);
    }
}
