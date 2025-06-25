using DAL.Models;

namespace DAL.Interfaces
{
    public interface IParserDateLimitDal
    {
        Task<List<ParserDateLimit>> GetAllAsync(CancellationToken ct);
        Task<DateOnly?> GetStopDateAsync(string parserName, CancellationToken ct);
        Task SetStopDateAsync(string parserName, DateOnly date, CancellationToken ct);
    }
}
