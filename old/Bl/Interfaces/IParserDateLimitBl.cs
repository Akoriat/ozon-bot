using DAL.Models;

namespace Bl.Interfaces
{
    public interface IParserDateLimitBl
    {
        Task<List<ParserDateLimit>> GetAllAsync(CancellationToken ct);
        Task<DateOnly?> GetStopDateAsync(string parserName, CancellationToken ct);
        Task SetStopDateAsync(string parserName, DateOnly date, CancellationToken ct);
    }
}
