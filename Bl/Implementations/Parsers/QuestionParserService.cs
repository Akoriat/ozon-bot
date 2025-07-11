using Bl.Gates;
using Bl.Interfaces;
using Common.Configuration.Configs;
using DAL.Models;
using Entities.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Bl.Implementations.Parsers;

public class QuestionParserService : IQuestionParserService
{
    private readonly SeleniumGate _gate;
    private readonly TimeSpan _defaultWait;
    private readonly ILogger<QuestionParserService> _logger;
    private readonly WebDriverWait _wait;
    private readonly string _sellerName;

    public QuestionParserService(SeleniumGate gate, ILogger<QuestionParserService> logger, IOptions<SellerConfig> sellerOptions)
    {
        _gate = gate;
        _logger = logger;
        _sellerName = sellerOptions.Value.SellerName;
        _defaultWait = TimeSpan.FromSeconds(3);

        _wait = _gate.RunSync(d =>
        {
            d.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(2);
            d.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(600);
            d.Manage().Timeouts().AsynchronousJavaScript = TimeSpan.FromSeconds(600);
            return new WebDriverWait(d, TimeSpan.FromSeconds(10));
        });
    }

    public Task Navigate(string url, int maxAttempts = 3, int waitSeconds = 30)
    {
        _gate.RunSync(d =>
        {
            d.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(waitSeconds);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    d.Navigate().GoToUrl(url);
                    return;
                }
                catch (Exception ex) when (attempt < maxAttempts)
                {
                    _logger.LogError(ex, "Попытка {attempt} завершилась с ошибкой", attempt);
                    Thread.Sleep(1000);
                }
            }

            throw new TimeoutException($"Не удалось загрузить {url} за {maxAttempts} попыток.");
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Устанавливает фильтр по диапазону дат вида "дд.мм.гггг – дд.мм.гггг".
    /// </summary>
    public Task SetDateRange(string range)
    {
        _gate.RunSync(d =>
        {
            SetDateRangeCore(d, range);
        });
        return Task.CompletedTask;
    }

    private static void SetDateRangeCore(IWebDriver webDriver, string range)
    {
        var wait = new WebDriverWait(webDriver, TimeSpan.FromSeconds(2));
        var dateInput = wait.Until(driver => driver.FindElement(By.CssSelector("input[placeholder='дд.мм.гггг – дд.мм.гггг']")));
        dateInput.Clear();
        dateInput.SendKeys(range);
        dateInput.SendKeys(Keys.Enter);
    }

    /// <summary>
    /// Нажимает кнопку "Загрузить ещё", если она доступна, и повторяет это до тех пор, пока кнопка не исчезнет.
    /// Оптимизировано: динамическое ожидание появления новых строк вместо фиксированного Thread.Sleep.
    /// </summary>
    public Task ClickLoadMoreRepeatedly()
    {
        _gate.RunSync(d =>
        {
            ClickLoadMoreRepeatedlyCore(d);
        });
        return Task.CompletedTask;
    }

    private void ClickLoadMoreRepeatedlyCore(IWebDriver d)
    {
        var wait = new WebDriverWait(d, _defaultWait);
        while (true)
        {
            try
            {
                bool success = false;
                int attempts = 0;
                while (!success && attempts < 3)
                {
                    attempts++;
                    try
                    {
                        var btn = d.FindElement(By.CssSelector("#app > main > div[class*='index_content_'] > div[class*='index_wrapper_'] div[class*='index_busyBox_'] > div > div[class*='index_controls_'] > button"));

                        int initialRowCount = WaitForStableTableRowsCore(d).Count;

                        btn.Click();

                        bool newRowsLoaded = wait.Until(driver =>
                        {
                            int newCount = WaitForStableTableRowsCore(d).Count;
                            return newCount > initialRowCount;
                        });
                        success = true;
                    }
                    catch (StaleElementReferenceException)
                    {
                        if (attempts == 3)
                        {
                            throw;
                        }
                    }
                }
            }
            catch (NoSuchElementException)
            {
                break;
            }
            catch (WebDriverTimeoutException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Ждёт, пока число видимых строк таблицы не стабилизируется.
    /// Оптимизировано: уменьшен интервал ожидания для проверки устойчивости.
    /// </summary>
    public Task<List<IWebElement>> WaitForStableTableRows()
    {
        var result = _gate.RunSync(d => WaitForStableTableRowsCore(d));
        return Task.FromResult(result);
    }

    private static List<IWebElement> WaitForStableTableRowsCore(IWebDriver webDriver)
    {
        Thread.Sleep(400);
        var table = webDriver.FindElements(By.CssSelector("table[class*='table-module_table_']")).First();
        Thread.Sleep(400);
        return table.FindElements(By.CssSelector("tbody tr")).Where(r => r.Displayed && r.Enabled).ToList();
    }

    /// <summary>
    /// Извлекает диалог из модального окна.
    /// Парсит только имена авторов и текст сообщений.
    /// Оптимизировано: используется один JavaScript-вызов для извлечения всех сообщений,
    /// а ожидание закрытия модального окна сокращено до 3 секунд.
    /// </summary>
    private string ExtractChatConversation(IWebDriver d, IWebElement questionButton, out string clientName, out string article, out bool inTopic)
    {
        inTopic = true;
        article = string.Empty;
        clientName = "ИмяОтсутствует";
        string conversation = string.Empty;
        try
        {
            IJavaScriptExecutor js = (IJavaScriptExecutor)d;
            js.ExecuteScript("arguments[0].click();", questionButton);

            var wait = new WebDriverWait(d, _defaultWait);
            wait.Until(driver => driver.FindElement(By.CssSelector("[class*='side-page-module_sidePage_']")));

            IWebElement? modal = null;
            try
            {
                modal = d.FindElement(By.CssSelector("[class*='side-page-module_sidePage_']"));
            }
            catch (NoSuchElementException)
            {
                Console.WriteLine("Модальное окно не найдено.");
            }

            var clientNameDivs = modal!.FindElements(By.CssSelector("div[class*='index_infoItem_']"));
            clientName = WaitForElement(clientNameDivs[5], "div:nth-of-type(2)", 3)?.Text ?? "ИмяОтсутствует";

            var container = modal.FindElement(By.XPath(
                "//div[contains(@class,'index_infoItem')][.//div[text()='Артикул']]"
            ));

            var valueDiv = container.FindElement(By.XPath("./div[2]"));
            article = valueDiv.Text.Trim();

            conversation = string.Empty;
            if (modal != null)
            {
                bool hasUnpublished = modal.FindElements(By.XPath(".//*[contains(normalize-space(),'не опубликован')]")).Count != 0;
                if (hasUnpublished)
                {
                    inTopic = true;
                    return conversation;
                }

                var messages = new List<string>();
                var answers = modal.FindElements(By.CssSelector("div[id*='answer-']"));
                if (answers.Count == 0)
                    return conversation;

                var lastAnswer = answers.Last();
                var alt = lastAnswer.FindElement(By.CssSelector("div[class*='index_imageRoot_'][class*='index_loaded_']")).GetAttribute("alt");
                if (alt == _sellerName || alt.Contains(_sellerName))
                {
                    inTopic = false;
                }

                foreach (var answer in answers)
                {
                    var name = answer.FindElement(By.CssSelector("div[class*='text-view-module_textView_'][class*='text-view-module_headline-h4_'][class*='typography-module_heading-200_'][class*='text-view-module_light_'][class*='text-view-module_paddingTopOff_'][class*='text-view-module_paddingBottomOff_']"));
                    var message = answer.FindElement(By.CssSelector("div[class*='text-view-module_textView_'][class*='text-view-module_paragraph-medium_'][class*='typography-module_body-500_']"));
                    if (name.Text.Contains(_sellerName))
                    {
                        inTopic = false;
                    }
                    else
                    {
                        inTopic = true;
                    }
                    messages.Add($"Имя:{name.Text.Trim()} Сообщение#:{message.Text}");
                }

                conversation = string.Join("\n---\n", messages);
            }

            var bodyElement = d.FindElement(By.TagName("body"));
            bodyElement.SendKeys(Keys.Escape);

            Thread.Sleep(200);
            return conversation;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Ошибка при извлечении переписки: " + ex.Message);
            return conversation;
        }
    }

    /// <summary>
    /// Извлекает данные из каждой видимой строки таблицы.
    /// Для каждой строки производится повторный поиск свежего элемента из DOM (с несколькими попытками),
    /// затем извлекается текст всех ячеек и, для колонки "Вопрос" (4-я ячейка),
    /// кликом по кнопке извлекается диалог из модального окна, который добавляется в конец строки.
    /// </summary>
    public List<InTopicModelDto<QuestionRecord>> ExtractTableData(DateOnly date, TimeOnly time)
    {
        return _gate.RunSync(d =>
        {
            ClickLoadMoreRepeatedlyCore(d);
            var records = new List<InTopicModelDto<QuestionRecord>>();
            var initialRows = WaitForStableTableRowsCore(d);
            int rowCount = initialRows.Count;

            for (int i = 0; i < rowCount; i++)
            {
                IWebElement? row = null;
                int attempts = 0;
                bool success = false;
                while (!success && attempts < 3)
                {
                    try
                    {
                        var currentRows = _wait.Until(x =>
                        {
                            var el = x.FindElements(By.CssSelector("table[class*='table-module_table_'] tbody tr"));
                            return el.All(x => x.Enabled && x.Displayed) ? el : null;
                        });

                        row = currentRows[i];
                        success = true;
                    }
                    catch (StaleElementReferenceException)
                    {
                        attempts++;
                        Thread.Sleep(100);
                    }
                }
                if (!success)
                {
                    continue;
                }

                attempts = 0;
                success = false;
                ReadOnlyCollection<IWebElement>? cells = null;
                while (!success && attempts < 3)
                {
                    try
                    {
                        cells = _wait.Until(x =>
                        {
                            var el = row!.FindElements(By.TagName("td"));
                            return el.All(x => x.Enabled && x.Displayed) ? el : null;
                        });
                        success = true;
                    }
                    catch (StaleElementReferenceException)
                    {
                        attempts++;
                        Thread.Sleep(200);
                        try
                        {
                            var currentRows = _wait.Until(x =>
                            {
                                var el = x.FindElements(By.CssSelector("table[class*='table-module_table_'] tbody tr"));
                                return el.All(x => x.Enabled && x.Displayed) ? el : null;
                            });
                            row = currentRows[i];
                        }
                        catch { }
                    }
                }
                if (!success)
                {
                    Console.WriteLine("Ошибка при извлечении ячеек строки: не удалось получить данные после нескольких попыток.");
                    continue;
                }

                try
                {
                    DateOnly dateValue = DateOnly.MinValue;
                    TimeOnly timeValue = TimeOnly.MinValue;
                    for (int j = 0; j < 3; j++)
                    {
                        try
                        {
                            if (cells!.Count == 0)
                            {
                                cells = _wait.Until(x =>
                                {
                                    var el = row!.FindElements(By.TagName("td"));
                                    return el.All(x => x.Enabled && x.Displayed) ? el : null;
                                });
                            }
                            var dateCell = cells[0];
                            var dateDivs = _wait.Until(x =>
                            {
                                var el = dateCell.FindElements(By.TagName("div"));
                                return el.All(el => el.Enabled && el.Displayed) ? el : null;
                            });

                            string dateText = dateDivs[0].Text.Trim();
                            string timeText = dateDivs[1].Text.Trim();

                            dateValue = DateOnly.ParseExact(dateText, "dd.MM.yyyy", CultureInfo.InvariantCulture);
                            timeValue = TimeOnly.ParseExact(timeText, "HH:mm", CultureInfo.InvariantCulture);

                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "не получилось спарсить дату");
                            continue;
                        }
                    }

                    if (dateValue == date && timeValue <= time)
                        break;

                    var productCell = cells![2];
                    var productLink = productCell.FindElement(By.TagName("a"));
                    string product = productLink.Text.Trim();

                    string questionText = "";
                    string chatConversation = "";
                    attempts = 0;
                    success = false;
                    bool inTopic = true;
                    string clientName = "";
                    string article = "";
                    while (!success && attempts < 3)
                    {
                        try
                        {
                            var questionCell = row!.FindElement(By.XPath(".//td[4]"));
                            var questionButton = questionCell.FindElement(By.TagName("button"));
                            questionText = questionButton.Text.Trim();
                            chatConversation = ExtractChatConversation(d, questionButton, out clientName, out article, out inTopic);
                            if (string.IsNullOrWhiteSpace(clientName))
                                clientName = "ИмяОтсутствует";
                            var bodyElement = d.FindElement(By.TagName("body"));
                            bodyElement.SendKeys(Keys.Escape);
                            success = true;
                        }
                        catch (StaleElementReferenceException)
                        {
                            attempts++;
                            Thread.Sleep(100);
                            try
                            {
                                var currentRows = d.FindElements(By.CssSelector("table[class*='table-module_table_'] tbody tr")).Where(r => r.Enabled && r.Displayed).ToList();
                                row = currentRows[i];
                            }
                            catch { }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Ошибка при извлечении чата для строки: " + ex.Message);
                            questionText = "";
                            chatConversation = "";
                            success = true;
                        }
                    }

                    string answers = cells.Count > 4 ? cells[4].Text.Trim() : "";
                    string usefulness = cells.Count > 5 ? cells[5].Text.Trim() : "";

                    var record = new InTopicModelDto<QuestionRecord>
                    {
                        Model = new QuestionRecord
                        {
                            Date = dateValue,
                            Time = timeValue,
                            ClientName = clientName,
                            Product = product,
                            Question = questionText,
                            Answers = answers,
                            Usefulness = usefulness,
                            ChatConversation = chatConversation,
                            Article = article
                        },
                        InTopic = inTopic
                    };

                    records.Add(record);
                }
                catch
                {
                    return records;
                }
            }
            return records;
        });
    }

    /// <summary>
    /// Ожидает обновления значения поля даты до ожидаемого значения.
    /// </summary>
    public Task WaitForDateFieldUpdate(string expectedDate, int maxWaitSeconds)
    {
        _gate.RunSync(d =>
        {
            var wait = new WebDriverWait(d, TimeSpan.FromSeconds(maxWaitSeconds));
            try
            {
                wait.Until(driver =>
                {
                    var value = driver.FindElement(By.CssSelector("input[class*='base-input-module_baseInput_']")).GetAttribute("value");
                    return value.Contains(expectedDate);
                });
            }
            catch (WebDriverTimeoutException)
            {
                Console.WriteLine("Не удалось обновить значение поля даты до " + expectedDate);
            }
        });
        return Task.CompletedTask;
    }

    public bool SendMessageToClient(string requestId, string message)
    {
        return _gate.RunSync(d =>
        {
            DateOnly stopDate = new DateOnly(2022, 4, 28);
            TimeOnly stopTime = new TimeOnly(0, 0);
            var temp = requestId.Split('_');
            var findDate = DateOnly.ParseExact(temp[1], "dd.MM.yyyy", CultureInfo.InvariantCulture);
            var findTime = TimeOnly.ParseExact(temp[2], "HH:mm");
            var findClientName = temp[3];

            DateOnly startDate = DateOnly.FromDateTime(DateTime.Today);
            var currentDate = startDate;

            while (currentDate >= findDate)
            {
                string dateStrNow = findDate.AddDays(1).ToString("dd.MM.yyyy");
                string dateStrLast = findDate.ToString("dd.MM.yyyy");
                string rangeStr = $"{dateStrLast} – {dateStrNow}";

                bool dateParsedOk = false;
                int attempts = 0;
                while (!dateParsedOk && attempts < 3)
                {
                    attempts++;
                    try
                    {
                        SetDateRangeCore(d, rangeStr);
                        var rows = WaitForStableTableRowsCore(d);
                        if (rows == null || rows.Count == 0)
                        {
                            Console.WriteLine("Строки таблицы не найдены");
                        }
                        else
                        {
                            ClickLoadMoreRepeatedlyCore(d);
                            bool data = FindQuestionAndSendMessage(d, message, findClientName, findDate, findTime).GetAwaiter().GetResult();
                            if (data)
                            {
                                return true;
                            }
                        }
                        dateParsedOk = true;
                    }
                    catch
                    {
                        d.Navigate().Refresh();
                    }
                }

                currentDate = currentDate.AddDays(-1);
            }
            return false;
        });
    }

    private async Task<bool> FindQuestionAndSendMessage(IWebDriver d, string message, string findClientName, DateOnly findDate, TimeOnly findTime)
    {
        var initialRows = WaitForStableTableRowsCore(d);
        int rowCount = initialRows.Count;

        for (int i = 0; i < rowCount; i++)
        {
            IWebElement? row = null;
            int attempts = 0;
            bool success = false;
            while (!success && attempts < 3)
            {
                try
                {
                    var currentRows = d.FindElements(By.CssSelector("table[class*='table-module_table_'] tbody tr"))
                     .Where(r => r.Displayed)
                     .ToList();
                    row = currentRows[i];
                    success = true;
                }
                catch (StaleElementReferenceException)
                {
                    attempts++;
                    Thread.Sleep(50);
                }
            }
            if (!success)
            {
                Console.WriteLine($"Не удалось получить строку {i} после нескольких попыток.");
                continue;
            }

            attempts = 0;
            success = false;
            List<IWebElement>? cells = null;
            while (!success && attempts < 3)
            {
                try
                {
                    cells = row!.FindElements(By.TagName("td")).ToList();
                    success = true;
                }
                catch (StaleElementReferenceException)
                {
                    attempts++;
                    Thread.Sleep(50);
                    try
                    {
                        var currentRows = d.FindElements(By.CssSelector("table[class*='table-module_table_'] tbody tr"))
                     .Where(r => r.Displayed)
                     .ToList();
                        row = currentRows[i];
                    }
                    catch { }
                }
            }
            if (!success)
            {
                Console.WriteLine("Ошибка при извлечении ячеек строки: не удалось получить данные после нескольких попыток.");
                continue;
            }

            try
            {
                var dateCell = cells![0];
                var dateDivs = dateCell.FindElements(By.TagName("div")).ToList();
                string dateText = dateDivs.Count > 0 ? dateDivs[0].Text.Trim() : "";
                string timeText = dateDivs.Count > 1 ? dateDivs[1].Text.Trim() : "";

                DateOnly dateValue = DateOnly.ParseExact(dateText, "dd.MM.yyyy", CultureInfo.InvariantCulture);
                TimeOnly timeValue = TimeOnly.ParseExact(timeText, "HH:mm");

                if (dateValue != findDate || dateValue == findDate && timeValue != findTime)
                    continue;

                attempts = 0;
                success = false;
                while (!success && attempts < 3)
                {
                    try
                    {
                        var questionCell = row!.FindElement(By.XPath(".//td[4]"));
                        var questionButton = questionCell.FindElement(By.TagName("button"));
                        var result = await TrySendMessageInChatConversationCore(d, questionButton, findClientName, message);
                        if (result)
                        {
                            return true;
                        }
                        else
                        {
                            var closeButton = d.FindElement(By.CssSelector("#ui-kit-side-page-portal-container > div > div > div > button"));
                            closeButton.Click();
                        }
                        success = true;
                    }
                    catch (StaleElementReferenceException)
                    {
                        attempts++;
                        Thread.Sleep(50);
                        try
                        {
                            var currentRows = d.FindElements(By.CssSelector("table[class*='table-module_table_'] tbody tr"))
                     .Where(r => r.Displayed)
                     .ToList();
                            row = currentRows[i];
                        }
                        catch { }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Ошибка при извлечении чата для строки: " + ex.Message);
                        success = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка при обработке строки " + i + ": " + ex.Message);
            }
        }
        return false;
    }

    private Task<bool> TrySendMessageInChatConversationCore(IWebDriver d, IWebElement questionButton, string clientName, string message)
    {
        try
        {
            IJavaScriptExecutor js = (IJavaScriptExecutor)d;
            js.ExecuteScript("arguments[0].click();", questionButton);

            Thread.Sleep(100);

            var wait = new WebDriverWait(d, _defaultWait);
            wait.Until(driver => driver.FindElement(By.CssSelector("[class*='side-page-module_sidePage_']")));

            IWebElement? modal = null;
            try
            {
                modal = d.FindElement(By.CssSelector("[class*='side-page-module_sidePage_']"));
            }
            catch (NoSuchElementException)
            {
                Console.WriteLine("Модальное окно не найдено.");
            }

            if (modal != null)
            {
                var clientNameDivs = modal.FindElements(By.CssSelector("div[class*='index_infoItem_']"));
                var findClientName = clientNameDivs[5].FindElement(By.CssSelector("div:nth-of-type(2)")).Text;

                if (clientName == findClientName.Trim() || clientName == "ИмяОтсутствует" && string.IsNullOrEmpty(findClientName))
                {
                    var area = modal.FindElement(By.CssSelector("textarea[id*='baseInput___']"));
                    try { area.Click(); }
                    catch (ElementClickInterceptedException)
                    {
                        ((IJavaScriptExecutor)d).ExecuteScript("arguments[0].click();", area);
                    }
                    Thread.Sleep(100);
                    string js_input = @"arguments[0].value += arguments[1];arguments[0].dispatchEvent(new Event('input', { bubbles:true }));";
                    ((IJavaScriptExecutor)d).ExecuteScript(js_input, area, message);
                    Thread.Sleep(100);
                    var btn = modal.FindElement(By.XPath("//button[.//span[normalize-space(text())='Отправить ответ']]"));
                    try
                    {
                        btn.Click();
                    }
                    catch (ElementClickInterceptedException)
                    {
                        ((IJavaScriptExecutor)d).ExecuteScript("arguments[0].click();", btn);
                    }
                    Thread.Sleep(100);
                    return Task.FromResult(true);
                }
                else
                {
                    return Task.FromResult(false);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Ошибка при извлечении переписки: " + ex.Message);
            return Task.FromResult(false);
        }

        return Task.FromResult(false);
    }

    private static IWebElement? WaitForElement(IWebElement webElement, string cssSelector, int maxAttempts)
    {
        IWebElement? element = null;
        int attempts = 0;
        while (attempts < maxAttempts)
        {
            try
            {
                element = webElement.FindElement(By.CssSelector(cssSelector));
                if (element.Enabled && element.Displayed) return element;
                attempts++;
            }
            catch
            {
                Thread.Sleep(500);
                attempts++;
            }
        }
        return element;
    }

    public bool RefreshActiveTopic(string requestId)
    {
        return _gate.RunSync(d =>
        {
            DateOnly stopDate = new DateOnly(2022, 4, 28);
            TimeOnly stopTime = new TimeOnly(0, 0);

            var temp = requestId.Split('_');
            var findDate = DateOnly.ParseExact(temp[1], "dd.MM.yyyy", CultureInfo.InvariantCulture);
            var findTime = TimeOnly.ParseExact(temp[2], "HH:mm");
            var findClientName = temp[3];

            DateOnly startDate = DateOnly.FromDateTime(DateTime.Today);
            var currentDate = startDate;

            while (currentDate >= stopDate)
            {
                string dateStrNow = findDate.AddDays(1).ToString("dd.MM.yyyy");
                string dateStrLast = findDate.ToString("dd.MM.yyyy");
                string rangeStr = $"{dateStrLast} – {dateStrNow}";

                bool dateParsedOk = false;
                int attempts = 0;
                while (!dateParsedOk && attempts < 3)
                {
                    attempts++;
                    try
                    {
                        SetDateRangeCore(d, rangeStr);
                        var rows = WaitForStableTableRowsCore(d);
                        if (rows == null || rows.Count == 0)
                        {
                            Console.WriteLine("Строки таблицы не найдены");
                        }
                        else
                        {
                            ClickLoadMoreRepeatedlyCore(d);
                            bool data = RefreshActiveTopicParseIteration(d, requestId, out bool refresh);
                            if (data)
                            {
                                return refresh;
                            }
                        }
                        dateParsedOk = true;
                    }
                    catch
                    {
                        d.Navigate().Refresh();
                    }
                }

                currentDate = currentDate.AddDays(-1);
            }
            return false;
        });
    }

    private bool RefreshActiveTopicParseIteration(IWebDriver d, string reqId, out bool refresh)
    {
        refresh = false;
        bool result = false;
        var temp = reqId.Split('_');
        var findDate = DateOnly.ParseExact(temp[1], "dd.MM.yyyy", CultureInfo.InvariantCulture);
        var findTime = TimeOnly.ParseExact(temp[2], "HH:mm");
        var findClientName = temp[3];

        var initialRows = WaitForStableTableRowsCore(d);
        int rowCount = initialRows.Count;

        for (int i = 0; i < rowCount; i++)
        {
            IWebElement? row = null;
            int attempts = 0;
            bool success = false;
            while (!success && attempts < 3)
            {
                try
                {
                    var currentRows = d.FindElements(By.CssSelector("table[class*='table-module_table_'] tbody tr"))
                     .Where(r => r.Displayed)
                     .ToList();
                    row = currentRows[i];
                    success = true;
                }
                catch (StaleElementReferenceException)
                {
                    attempts++;
                    Thread.Sleep(50);
                }
            }
            if (!success)
            {
                Console.WriteLine($"Не удалось получить строку {i} после нескольких попыток.");
                continue;
            }

            attempts = 0;
            success = false;
            List<IWebElement>? cells = null;
            while (!success && attempts < 3)
            {
                try
                {
                    cells = row!.FindElements(By.TagName("td")).ToList();
                    success = true;
                }
                catch (StaleElementReferenceException)
                {
                    attempts++;
                    Thread.Sleep(50);
                    try
                    {
                        var currentRows = d.FindElements(By.CssSelector("table[class*='table-module_table_'] tbody tr"))
                     .Where(r => r.Displayed)
                     .ToList();
                        row = currentRows[i];
                    }
                    catch { }
                }
            }
            if (!success)
            {
                Console.WriteLine("Ошибка при извлечении ячеек строки: не удалось получить данные после нескольких попыток.");
                continue;
            }

            try
            {
                var dateCell = cells![0];
                var dateDivs = dateCell.FindElements(By.TagName("div")).ToList();
                string dateText = dateDivs.Count > 0 ? dateDivs[0].Text.Trim() : "";
                string timeText = dateDivs.Count > 1 ? dateDivs[1].Text.Trim() : "";

                DateOnly dateValue = DateOnly.ParseExact(dateText, "dd.MM.yyyy", CultureInfo.InvariantCulture);
                TimeOnly timeValue = TimeOnly.ParseExact(timeText, "HH:mm");

                if (dateValue != findDate || dateValue == findDate && timeValue != findTime)
                {
                    if (dateValue < findDate || dateValue == findDate && timeValue < findTime)
                    {
                        refresh = true;
                        return true;
                    }
                    continue;
                }

                attempts = 0;
                success = false;
                while (!success && attempts < 3)
                {
                    try
                    {
                        var questionCell = row!.FindElement(By.XPath(".//td[4]"));
                        var questionButton = questionCell.FindElement(By.TagName("button"));
                        result = TryRefreshInChatConversation(d, questionButton, findClientName, out refresh);
                        if (result)
                        {
                            return refresh;
                        }
                        else
                        {
                            var closeButton = d.FindElement(By.CssSelector("#ui-kit-side-page-portal-container > div > div > div > button"));
                            closeButton.Click();
                        }
                        success = true;
                    }
                    catch (StaleElementReferenceException)
                    {
                        attempts++;
                        Thread.Sleep(50);
                        try
                        {
                            var currentRows = d.FindElements(By.CssSelector("table[class*='table-module_table_'] tbody tr"))
                     .Where(r => r.Displayed)
                     .ToList();
                            row = currentRows[i];
                        }
                        catch { }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Ошибка при извлечении чата для строки: " + ex.Message);
                        success = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка при обработке строки " + i + ": " + ex.Message);
            }
        }
        return result;
    }

    private bool TryRefreshInChatConversation(IWebDriver d, IWebElement questionButton, string findClientName, out bool refresh)
    {
        refresh = false;
        try
        {
            IJavaScriptExecutor js = (IJavaScriptExecutor)d;
            js.ExecuteScript("arguments[0].click();", questionButton);

            var wait = new WebDriverWait(d, _defaultWait);
            wait.Until(driver => driver.FindElement(By.CssSelector("[class*='side-page-module_sidePage_']")));

            IWebElement? modal = null;
            try
            {
                modal = d.FindElement(By.CssSelector("[class*='side-page-module_sidePage_']"));
            }
            catch (NoSuchElementException)
            {
                Console.WriteLine("Модальное окно не найдено.");
            }

            var clientNameDivs = modal!.FindElements(By.CssSelector("div[class*='index_infoItem_']"));
            var clientName = WaitForElement(clientNameDivs[5], "div:nth-of-type(2)", 3)!.Text;
            if (string.IsNullOrEmpty(clientName))
            {
                clientName = "ИмяОтсутствует";
            }

            if (clientName != findClientName)
            {
                if (findClientName == "ИмяОтсутствует" && !string.IsNullOrEmpty(clientName))
                    findClientName = clientName;
                else
                    return false;
            }

            var container = modal.FindElement(By.XPath(
                "//div[contains(@class,'index_infoItem')][.//div[text()='Артикул']]"
            ));

            var valueDiv = container.FindElement(By.XPath("./div[2]"));

            string conversation = string.Empty;
            if (modal != null)
            {
                bool hasUnpublished = modal.FindElements(By.XPath(".//*[contains(normalize-space(),'не опубликован')]")).Count != 0;
                if (hasUnpublished)
                {
                    refresh = true;
                    return true;
                }

                var messages = new List<string>();
                var answers = modal.FindElements(By.CssSelector("div[id*='answer-']"));
                if (answers.Count == 0)
                {
                    refresh = false;
                    return true;
                }

                var lastAnswer = answers.Last();
                var alt = lastAnswer.FindElement(By.CssSelector("div[class*='index_imageRoot_'][class*='index_loaded_']")).GetAttribute("alt");
                if (alt == _sellerName || alt.Contains(_sellerName))
                {
                    refresh = true;
                }

                foreach (var answer in answers)
                {
                    var name = answer.FindElement(By.CssSelector("div[class*='text-view-module_textView_'][class*='text-view-module_headline-h4_'][class*='typography-module_heading-200_'][class*='text-view-module_light_'][class*='text-view-module_paddingTopOff_'][class*='text-view-module_paddingBottomOff_']"));
                    var message = answer.FindElement(By.CssSelector("div[class*='text-view-module_textView_'][class*='text-view-module_paragraph-medium_'][class*='typography-module_body-500_']"));
                    if (name.Text.Contains(_sellerName))
                    {
                        refresh = true;
                    }
                    else
                    {
                        refresh = false;
                    }
                    messages.Add($"Имя:{name.Text.Trim()} Сообщение#:{message.Text}");
                }

                conversation = string.Join("\n---\n", messages);
            }

            var closeButton = d.FindElement(By.CssSelector("[class*='side-page-module_closeIconBtn_']"));
            js.ExecuteScript("arguments[0].click();", closeButton);

            Thread.Sleep(200);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Ошибка при извлечении переписки: " + ex.Message);
            return false;
        }
    }

    public Task ProcessedButtonClick()
    {
        _gate.RunSync(d =>
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    var button = d.FindElement(By.XPath("//*[@id=\"app\"]/main/div[1]/div[1]/div/div/div[3]/div[1]/button[4]"));
                    button.Click();
                    return;
                }
                catch
                {
                    Thread.Sleep(1000);
                }
            }
        });
        return Task.CompletedTask;
    }

    public Task<List<InTopicModelDto<QuestionRecord>>> ExtractTableDataForAllRefresh(DateOnly date, TimeOnly time)
    {
        var result = _gate.RunSync(d =>
        {
            ClickLoadMoreRepeatedlyCore(d);

            var records = new List<InTopicModelDto<QuestionRecord>>();
            var initialRows = WaitForStableTableRowsCore(d);
            int rowCount = initialRows.Count;

            for (int i = 0; i < rowCount; i++)
            {
                IWebElement? row = null;
                int attempts = 0;
                bool success = false;
                while (!success && attempts < 3)
                {
                    try
                    {
                        var currentRows = _wait.Until(x =>
                        {
                            var el = x.FindElements(By.CssSelector("table[class*='table-module_table_'] tbody tr"));
                            return el.All(x => x.Enabled && x.Displayed) ? el : null;
                        });

                        row = currentRows[i];
                        success = true;
                    }
                    catch (StaleElementReferenceException)
                    {
                        attempts++;
                        Thread.Sleep(100);
                    }
                }
                if (!success)
                {
                    continue;
                }

                attempts = 0;
                success = false;
                ReadOnlyCollection<IWebElement>? cells = null;
                while (!success && attempts < 3)
                {
                    try
                    {
                        cells = _wait.Until(x =>
                        {
                            var el = row!.FindElements(By.TagName("td"));
                            return el.All(x => x.Enabled) ? el : null;
                        });
                        success = true;
                    }
                    catch (StaleElementReferenceException)
                    {
                        attempts++;
                        Thread.Sleep(200);
                        try
                        {
                            var currentRows = _wait.Until(x =>
                            {
                                var el = x.FindElements(By.CssSelector("table[class*='table-module_table_'] tbody tr"));
                                return el.All(x => x.Enabled && x.Displayed) ? el : null;
                            });
                            row = currentRows[i];
                        }
                        catch { }
                    }
                }
                if (!success)
                {
                    Console.WriteLine("Ошибка при извлечении ячеек строки: не удалось получить данные после нескольких попыток.");
                    continue;
                }

                try
                {
                    DateOnly dateValue = DateOnly.MinValue;
                    TimeOnly timeValue = TimeOnly.MinValue;
                    for (int j = 0; j < 3; j++)
                    {
                        try
                        {
                            if (cells!.Count == 0)
                            {
                                cells = _wait.Until(x =>
                                {
                                    var el = row!.FindElements(By.TagName("td"));
                                    return el.All(x => x.Enabled && x.Displayed) ? el : null;
                                });
                            }
                            var dateCell = cells[0];
                            var dateDivs = _wait.Until(x =>
                            {
                                var el = dateCell.FindElements(By.TagName("div"));
                                return el.All(el => el.Enabled && el.Displayed) ? el : null;
                            });

                            string dateText = dateDivs[0].Text.Trim();
                            _logger.LogWarning(dateText);
                            string timeText = dateDivs[1].Text.Trim();
                            _logger.LogWarning(timeText);

                            dateValue = DateOnly.ParseExact(dateText, "dd.MM.yyyy", CultureInfo.InvariantCulture);
                            timeValue = TimeOnly.ParseExact(timeText, "HH:mm", CultureInfo.InvariantCulture);

                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "не получилось спарсить дату");
                            continue;
                        }
                    }

                    if (dateValue == date && timeValue <= time)
                        break;

                    var productCell = cells![2];
                    var productLink = productCell.FindElement(By.TagName("a"));
                    string product = productLink.Text.Trim();

                    string questionText = "";
                    string chatConversation = "";
                    attempts = 0;
                    success = false;
                    bool inTopic = true;
                    string clientName = "";
                    string article = "";
                    while (!success && attempts <= 3)
                    {
                        try
                        {
                            var questionCell = row!.FindElement(By.XPath(".//td[4]"));
                            var questionButton = questionCell.FindElement(By.TagName("button"));
                            questionText = questionButton.Text.Trim();
                            chatConversation = ExtractChatConversation(d, questionButton, out clientName, out article, out inTopic);
                            if (string.IsNullOrWhiteSpace(clientName))
                                clientName = "ИмяОтсутствует";
                            var bodyElement = d.FindElement(By.TagName("body"));
                            bodyElement.SendKeys(Keys.Escape);
                            success = true;
                        }
                        catch (StaleElementReferenceException)
                        {
                            attempts++;
                            Thread.Sleep(100);
                            try
                            {
                                var currentRows = d.FindElements(By.CssSelector("table[class*='table-module_table_'] tbody tr"))
                                    .Where(r => r.Enabled && r.Displayed).ToList();
                                row = currentRows[i];
                            }
                            catch
                            {
                                if (attempts > 3)
                                {
                                    _logger.LogError("Не удалось получить строку после нескольких попыток.");
                                    questionText = "";
                                    chatConversation = "";
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Ошибка при извлечении чата для строки: ");
                            questionText = "";
                            chatConversation = "";
                            success = true;
                        }
                    }

                    string answers = cells.Count > 4 ? cells[4].Text.Trim() : "";
                    string usefulness = cells.Count > 5 ? cells[5].Text.Trim() : "";

                    var record = new InTopicModelDto<QuestionRecord>
                    {
                        Model = new QuestionRecord
                        {
                            Date = dateValue,
                            Time = timeValue,
                            ClientName = clientName,
                            Product = product,
                            Question = questionText,
                            Answers = answers,
                            Usefulness = usefulness,
                            ChatConversation = chatConversation,
                            Article = article
                        },
                        InTopic = inTopic
                    };

                    records.Add(record);
                }
                catch
                {
                    return records;
                }
            }
            return records;
        });
        return Task.FromResult(result);
    }

    public async ValueTask DisposeAsync()
    {
        _gate.RunSync(d =>
        {
            d.Quit();
        });
        await Task.CompletedTask;
    }
}

