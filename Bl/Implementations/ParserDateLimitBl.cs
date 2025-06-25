using Bl.Interfaces;
using DAL.Interfaces;
using DAL.Models;

namespace Bl.Implementations
{
    public class ParserDateLimitBl : IParserDateLimitBl
    {
        private readonly IParserDateLimitDal _parserDateLimitDal;
        public ParserDateLimitBl(IParserDateLimitDal parserDateLimitDal)
        {
            _parserDateLimitDal = parserDateLimitDal;
        }

        public Task<List<ParserDateLimit>> GetAllAsync(CancellationToken ct)
            => _parserDateLimitDal.GetAllAsync(ct);

        public Task<DateOnly?> GetStopDateAsync(string parserName, CancellationToken ct)
            => _parserDateLimitDal.GetStopDateAsync(parserName, ct);

        public Task SetStopDateAsync(string parserName, DateOnly date, CancellationToken ct)
            => _parserDateLimitDal.SetStopDateAsync(parserName, date, ct);
    }
}
