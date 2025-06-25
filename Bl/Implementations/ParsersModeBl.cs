using Bl.Common.Enum;
using Bl.Interfaces;
using DAL.Interfaces;
using DAL.Models;

namespace Bl.Implementations
{
    public class ParsersModeBl : IParsersModeBl
    {
        private readonly IParsersModeDal _parserModeDal;
        public ParsersModeBl(IParsersModeDal parsersModeDal) { _parserModeDal = parsersModeDal; }
        public async Task<List<ParsersMode>> GetAllModesAsync(CancellationToken ct)
        {
            return await _parserModeDal.GetAllModesAsync(ct);
        }

        public async Task<bool> ToggleModeAsync(ParserType parserType, CancellationToken ct)
        {
            return await _parserModeDal.ToggleModeAsync(parserType.ToString(), ct);
        }
    }
}
