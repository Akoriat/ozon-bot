using DAL.Models;
using Entities.DTOs;
using OpenQA.Selenium;

namespace Bl.Interfaces;

public interface IQuestionParserService
{
    public Task Navigate(string url, int maxAttempts = 3, int waitSeconds = 30);
    public Task WaitForDateFieldUpdate(string expectedDate, int maxWaitSeconds);
    public Task<List<IWebElement>> WaitForStableTableRows();
    public Task ClickLoadMoreRepeatedly();
    public Task SetDateRange(string range);
    public List<InTopicModelDto<QuestionRecord>> ExtractTableData(DateOnly date, TimeOnly time);
    public Task<List<InTopicModelDto<QuestionRecord>>> ExtractTableDataForAllRefresh(DateOnly date, TimeOnly time);
    public bool SendMessageToClient(string requestId, string message);
    public bool RefreshActiveTopic(string requestId);
    public Task ProcessedButtonClick();
}
