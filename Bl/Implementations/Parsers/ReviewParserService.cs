using Bl.Gates;
using Bl.Interfaces;
using Common.Configuration.Configs;
using DAL.Models;
using Entities.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;
using System.Globalization;

namespace Bl.Implementations.Parsers;

public class ReviewParserService : IOzonReviewParserService
{
    private readonly SeleniumGate _gate;
    private readonly WebDriverWait _wait;
    private readonly ILogger<ReviewParserService> _logger;
    private readonly string _sellerName;
    private static readonly string[] separator = new[] { "\r\n", "\n", "\r" };

    public ReviewParserService(SeleniumGate gate, ILogger<ReviewParserService> logger, IOptions<SellerConfig> sellerOptions)
    {
        _gate = gate;
        _wait = _gate.RunSync(d => new WebDriverWait(d, TimeSpan.FromSeconds(10)));
        _logger = logger;
        _sellerName = sellerOptions.Value.SellerName;
    }
    public void Navigate(string url, int maxAttempts = 3, int waitSeconds = 30)
    {
        _gate.RunSync(d =>
        {
            int attempt = 0;
            bool isLoaded = false;

            while (attempt < maxAttempts && !isLoaded)
            {
                try
                {
                    _logger.LogInformation("Открываю URL {Url}. Попытка {Attempt}/{MaxAttempts}",
                                           url, attempt + 1, maxAttempts);

                    d.Navigate().GoToUrl(url);

                    var wait = new WebDriverWait(d, TimeSpan.FromSeconds(waitSeconds));
                    wait.Until(drv =>
                    {
                        try
                        {
                            return ((IJavaScriptExecutor)drv)
                                  .ExecuteScript("return document.readyState")
                                  .ToString()!.Equals("complete", StringComparison.OrdinalIgnoreCase);
                        }
                        catch (InvalidOperationException) { return false; }
                        catch (WebDriverException) { return false; }
                    });

                    isLoaded = true;
                    _logger.LogInformation("Страница {Url} успешно загружена", url);
                }
                catch (Exception ex)
                {
                    attempt++;
                    _logger.LogWarning(ex,
                        "Ошибка при загрузке {Url} (попытка {Attempt}/{MaxAttempts})",
                        url, attempt, maxAttempts);

                    if (attempt >= maxAttempts)
                    {
                        _logger.LogError(ex,
                            "Достигнут лимит попыток при загрузке {Url}. Прерываю.", url);
                        throw;
                    }

                    Task.Delay(2000).Wait();
                }
            }
        });
    }

    private DateTimeForParserDto? ParseDateTime(string text)
    {
        var parts = text.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;
        try
        {
            string dateText = parts[0].Trim();
            string timeText = parts[1].Trim();
            DateOnly date = DateOnly.ParseExact(dateText, "dd.MM.yyyy", CultureInfo.InvariantCulture);
            TimeOnly time = TimeOnly.ParseExact(timeText, "HH:mm", CultureInfo.InvariantCulture);
            return new DateTimeForParserDto { Date = date, Time = time };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
        "Не удалось разобрать дату/время из строки \"{Text}\"", text);
            return null;
        }
    }

    private ProcessModalFromReviewsParserDto ProcessModal(IWebDriver d, out bool inTopic, out string clientName)
    {
        clientName = "";
        inTopic = true;
        string reviewText = string.Empty;
        string dialog = string.Empty;
        var shortWait = new WebDriverWait(d, TimeSpan.FromSeconds(3));

        var modal = _wait.Until(ExpectedConditions.ElementIsVisible(
            By.CssSelector("div[class*='drawer-module_drawer_'] > div[class*='index_container_']")));

        if (modal == null)
        {
            _logger.LogWarning("Модальное окно не найдено.");
            return new ProcessModalFromReviewsParserDto { ReviewText = reviewText, Dialog = dialog };
        }

        Task.Delay(200).Wait();

        clientName = modal
            .FindElement(By.CssSelector("div[class*='index_reviewInfo_'] > div:nth-child(2) div[class*='index_content_'] > div"))
            .Text.Trim();

        try
        {
            IWebElement? reviewNode = null;
            try
            {
                var infoNodes = modal.FindElements(
                    By.CssSelector("div[class*='index_reviewInfo_'] > div[class*='index_info_']"));
                if (infoNodes.Count > 2)
                {
                    reviewNode = infoNodes[1]
                        .FindElements(By.CssSelector("div[class*='index_content_'] > div"))
                        .FirstOrDefault();
                }
            }
            catch (StaleElementReferenceException) { }

            if (reviewNode == null)
            {
                inTopic = false;
                return new ProcessModalFromReviewsParserDto { ReviewText = reviewText, Dialog = dialog };
            }

            if (reviewNode != null)
            {
                reviewText = reviewNode.Text.Trim();
            }
            else
            {
                _logger.LogWarning("Не удалось найти узел с текстом отзыва.");
            }
        }
        catch
        {
        }

        try
        {
            var comments = modal.FindElements(
                By.CssSelector("div[class*='index_comments_'] div[class*='index_ownerComment_']"));
            var list = new List<string>();
            foreach (var comment in comments)
            {
                inTopic = true;
                var user = comment
                    .FindElements(By.CssSelector("div[class*='index_header_'] div[class*='index_title_']"))
                    .FirstOrDefault()?.Text.Trim() ?? "";
                var text = comment
                    .FindElements(By.CssSelector("div[class*='index_commentText_']"))
                    .FirstOrDefault()?.Text.Trim() ?? "";

                if (!string.IsNullOrEmpty(user) || !string.IsNullOrEmpty(text))
                {
                    list.Add($"{user}: {text}");
                    if (user.Contains(_sellerName)) inTopic = false;
                }
            }
            dialog = string.Join("\n", list);
        }
        catch (Exception ex)
        {
            bool hasUnpublished = modal.FindElements(By.XPath(".//*[contains(normalize-space(),'не опубликован')]")).Any();
            if (hasUnpublished)
            {
                inTopic = true;
            }
            _logger.LogError(ex, "Ошибка при извлечении комментариев.");
        }

        Task.Delay(700).Wait();

        // Закрытие модального окна
        try
        {
            //_logger.LogInformation("Закрываю модальное окно нажатием ESC.");
            d.FindElement(By.TagName("body")).SendKeys(Keys.Escape);
        }
        catch (Exception ex) when (ex is NoSuchElementException || ex is WebDriverException)
        {
            _logger.LogWarning(ex, "Не удалось отправить ESC для закрытия модального окна.");
        }

        try
        {
            shortWait.Until(drv =>
                drv.FindElements(By.CssSelector("div[class*='drawer-module_drawer_'] > div[class*='index_container_']"))
                   .Count == 0);
            //_logger.LogInformation("Модальное окно успешно закрыто.");
        }
        catch (WebDriverTimeoutException)
        {
            _logger.LogWarning("Модальное окно не исчезло вовремя после ESC.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при ожидании исчезновения модального окна.");
        }

        return new ProcessModalFromReviewsParserDto
        {
            ReviewText = reviewText,
            Dialog = dialog
        };
    }

    public Task<(List<InTopicModelDto<Review>>, bool)> ParseIteration(DateOnly date, TimeOnly time, CancellationToken ct)
    {
        var tuple = _gate.RunSync(d => ParseIterationCore(d, date, time).GetAwaiter().GetResult(), ct);
        return Task.FromResult(tuple);
    }

    public Task<(List<InTopicModelDto<Review>>, bool)> ParseIterationCore(IWebDriver d, DateOnly date, TimeOnly time)
    {
        var hasMoreLocal = true;
        var reviews = new List<InTopicModelDto<Review>>();

        int totalRows = ScrollContainerToEnd(d,
                    "tbody tr[class*='index_row_']"
                );

        var rows = d.FindElements(By.CssSelector("tbody tr[class*='index_row_']")).ToList();
        if (rows.Count < totalRows)
        {
            rows = d.FindElements(By.CssSelector("tbody tr[class*='index_row_']")).ToList();
        }

        int counter = 0;
        foreach (var row in rows)
        {
            try
            {
                var cells = row.FindElements(By.TagName("td"));
                if (cells.Count < 11)
                    continue;

                // 1. Парсинг даты и времени
                var dateTimeDto = ParseDateTime(cells[10].Text);
                if (dateTimeDto == null)
                {
                    Thread.Sleep(300);
                    dateTimeDto = ParseDateTime(cells[10].Text);
                    if (dateTimeDto == null)
                        continue;
                }
                else if (dateTimeDto.Date <= date && dateTimeDto.Time <= time)
                {
                    hasMoreLocal = false;
                    return Task.FromResult<(List<InTopicModelDto<Review>>, bool)>((reviews, hasMoreLocal));
                }

                // 2. Статус
                string status = cells[5].Text.Trim();

                // 3. Товар (из ссылки)
                string product = "";
                string article = "";
                try
                {
                    product = cells[2].FindElement(By.TagName("p")).Text.Trim();
                    article = cells[2].FindElement(By.CssSelector("div div")).Text.Split("\r\n").LastOrDefault() ?? "";
                }
                catch (NoSuchElementException) { }

                // 4. Статус получения
                string receptionStatus = cells[3].Text.Trim();

                bool inTopic = true;
                string clientName = "";
                // 5. Отзыв и диалог (через модальное окно)
                var processModalFromReviewsParserDto = new ProcessModalFromReviewsParserDto { ReviewText = "", Dialog = "" };
                try
                {
                    var reviewButton = cells[4];
                    if (!string.IsNullOrEmpty(reviewButton.Text))
                    {
                        if (reviewButton.Enabled)
                            try { reviewButton.Click(); }
                            catch
                            {
                                ((IJavaScriptExecutor)d).ExecuteScript("arguments[0].click();", reviewButton);
                            }

                        try
                        {
                            processModalFromReviewsParserDto = ProcessModal(d, out inTopic, out clientName);
                            var bodyElement = d.FindElement(By.TagName("body"));
                            bodyElement.SendKeys(Keys.Escape);
                        }
                        catch
                        {
                            var bodyElement = d.FindElement(By.TagName("body"));
                            bodyElement.SendKeys(Keys.Escape);
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ошибка при обработке модального окна: ");
                    try
                    {
                        var bodyElement = d.FindElement(By.TagName("body"));
                        bodyElement.SendKeys(Keys.Escape);
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogError(innerEx, "Ошибка при закрытии модального окна");
                        throw;
                    }
                    continue;
                }

                int rating = 0;
                try
                {
                    var ratingDiv = cells[6].FindElement(By.CssSelector("div"));
                    int.TryParse(ratingDiv.Text.Trim(), out rating);
                }
                catch (NoSuchElementException) { }

                string photo = cells[7].Text.Trim();

                string video = cells[8].Text.Trim();

                string answers = cells[9].Text.Trim();

                reviews.Add(new InTopicModelDto<Review>
                {
                    Model = new Review
                    {
                        ReviewDate = dateTimeDto.Date,
                        ReviewTime = dateTimeDto.Time,
                        Status = status,
                        Product = product,
                        ReceptionStatus = receptionStatus,
                        ReviewText = processModalFromReviewsParserDto.ReviewText,
                        Rating = rating,
                        Photo = photo,
                        Video = video,
                        Answers = answers,
                        Dialog = processModalFromReviewsParserDto.Dialog,
                        Name = clientName,
                        Article = article
                    },
                    InTopic = inTopic
                });

                Thread.Sleep(200);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке строки");
            }
            counter++;
        }
        return Task.FromResult<(List<InTopicModelDto<Review>>, bool)>((reviews, hasMoreLocal));
    }

    public static string CssStartsWith(string tag, string classPrefix) =>
        $"{tag}[class^='{classPrefix}_']";

    public static int ScrollContainerToEnd(
        IWebDriver driver,
        string rowSelector,
        int timeoutSeconds = 5)
    {
        var js = (IJavaScriptExecutor)driver;
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
        int rows = 0;

        while (true)
        {
            var containerForScroll = driver.FindElement(By.TagName("body"));
            js.ExecuteScript(@"
    (document.scrollingElement || document.documentElement).scrollTop =
        (document.scrollingElement || document.documentElement).scrollHeight;");

            try
            {
                wait.Until(d =>
                {
                    int cur = d.FindElements(By.CssSelector(rowSelector)).Count;
                    bool grown = cur > rows;
                    if (grown) rows = cur;
                    return grown;
                });
            }
            catch (WebDriverTimeoutException)
            {
                break;   // новых строк не появилось за timeout → конец
            }
        }
        return rows;
    }

    /// <summary>
    /// Прокрутить до конца всю СТРАНИЦУ (если таблица цепляется к window.scroll).
    /// Возвращает финальное количество строк.
    /// </summary>
    public static int ScrollPageToEnd(
        IWebDriver driver,
        string rowSelector,
        int timeoutSeconds = 5)
    {
        var js = (IJavaScriptExecutor)driver;
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(timeoutSeconds));
        int rows = 0;

        while (true)
        {
            js.ExecuteScript("window.scrollTo(0, document.body.scrollHeight);");

            try
            {
                wait.Until(d =>
                {
                    int cur = d.FindElements(By.CssSelector(rowSelector)).Count;
                    bool grown = cur > rows;
                    if (grown) rows = cur;
                    return grown;
                });
            }
            catch (WebDriverTimeoutException)
            {
                break;
            }
        }
        return rows;
    }

    public bool ClickButtonCore(IWebDriver d)
    {
        var hasMore = true;
        try
        {
            // Ждем, пока оверлеи (если есть) исчезнут
            _wait.Until(drv =>
            {
                var overlays = drv.FindElements(By.CssSelector("div[class*='backdrop-module_backdrop_']"));
                return overlays.Count == 0 || overlays.All(o => !o.Displayed);
            });

            // Находим кнопку и ждём, пока она станет кликабельной
            var loadMoreButton = _wait.Until(ExpectedConditions.ElementToBeClickable(By.CssSelector("button[class*='button-module_button_'][class*='button-module_size-500_'][class*='index_showMoreBtn_']")));

            // Повторяем попытку клика до maxAttempts
            int attempts = 0;
            int maxAttempts = 3;
            bool clickSuccessful = false;

            while (attempts < maxAttempts && !clickSuccessful)
            {
                try
                {
                    // Пытаемся выполнить обычный клик
                    loadMoreButton.Click();
                    clickSuccessful = true;
                }
                catch (ElementClickInterceptedException)
                {
                    try
                    {
                        // Если обычный клик не срабатывает, выполняем JavaScript‑клик
                        ((IJavaScriptExecutor)d).ExecuteScript("arguments[0].click();", loadMoreButton);
                        clickSuccessful = true;
                    }
                    catch (Exception jsEx)
                    {
                        _logger.LogError(jsEx, "Ошибка при выполнении JavaScript‑клика");
                        Task.Delay(2000).Wait();
                        attempts++;
                    }
                }
            }

            if (!clickSuccessful)
            {
                hasMore = false;
                return hasMore;
            }
            Thread.Sleep(500);
            return hasMore;
        }
        catch (NoSuchElementException)
        {
            hasMore = false; return hasMore;
        }
        catch (WebDriverTimeoutException)
        {
            hasMore = false; return hasMore;
        }
    }

    public void ClickButton(out bool hasMore)
    {
        hasMore = _gate.RunSync(d => ClickButtonCore(d));
    }

    public bool SendMessageToClient(string requestId, string message, string article)
    {
        return _gate.RunSync(d =>
        {
            var temp = requestId.Split('_');
            var findDate = DateOnly.ParseExact(temp[1], "dd.MM.yyyy", CultureInfo.InvariantCulture);
            var findTime = TimeOnly.ParseExact(temp[2], "HH:mm");
            var findClientName = temp[3];

            ClickCalendarDateCore(d, findDate).GetAwaiter().GetResult();
            Thread.Sleep(600);
            SetArticleInSearchFieldCore(d, article);
            Thread.Sleep(600);
            int totalRows = ScrollContainerToEnd(d, "tbody tr[class*='index_row_']");
            var rows = d.FindElements(By.CssSelector("tbody tr[class*='index_row_']")).ToList();
            if (rows.Count < totalRows)
            {
                rows = d.FindElements(By.CssSelector("tbody tr[class*='index_row_']")).ToList();
            }

            foreach (var row in rows)
            {
                try
                {
                    var cells = row.FindElements(By.TagName("td"));
                    if (cells.Count < 11)
                        continue;

                    // 1. Парсинг даты и времени
                    var dateTimeDto = ParseDateTime(cells[10].Text);
                    if (dateTimeDto == null)
                    {
                        Thread.Sleep(300);
                        dateTimeDto = ParseDateTime(cells[10].Text);
                        if (dateTimeDto == null)
                            continue;
                    }
                    else if (dateTimeDto.Date != findDate || dateTimeDto.Time != findTime)
                    {
                        continue;
                    }

                    // 5. Отзыв и диалог (через модальное окно)
                    var processModalFromReviewsParserDto = new ProcessModalFromReviewsParserDto { ReviewText = "", Dialog = "" };
                    try
                    {
                        var reviewButton = cells[4];
                        if (reviewButton.Displayed && reviewButton.Enabled)
                            try { reviewButton.Click(); }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Ошибка при клике на кнопку отзыва, пробую через JS");
                                ((IJavaScriptExecutor)d).ExecuteScript("arguments[0].click();", reviewButton);
                            }
                        else
                            ((IJavaScriptExecutor)d).ExecuteScript("arguments[0].click();", reviewButton);

                        var tmp = ProcessModalToSendMessage(d, findClientName, message);
                        if (tmp)
                            return true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при обработке модального окна");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при обработке строки");
                }
            }
            return false;
        });
    }

    private bool ProcessModalToSendMessage(IWebDriver d, string clientName, string message)
    {
        var findClientName = "";
        string reviewText = string.Empty;
        string dialog = string.Empty;

        var shortWait = new WebDriverWait(d, TimeSpan.FromSeconds(3));

        try
        {
            var modal = _wait.Until(ExpectedConditions.ElementIsVisible(By.CssSelector("div[class*='drawer-module_drawer_'] > div[class*='index_container_']")));

            if (modal == null)
            {
                _logger.LogError("Модальное окно не найдено.");
                return false;
            }

            Thread.Sleep(1000);

            findClientName = modal.FindElement(By.CssSelector("div[class*='index_reviewInfo_'] > div:nth-child(2) div[class*='index_content_'] > div")).Text.Trim();

            if (findClientName != clientName)
            {
                if (!string.IsNullOrEmpty(clientName))
                    return false;
            }

            try
            {
                var textArea = modal.FindElement(By.CssSelector("textarea[id='AnswerCommentForm']"));
                textArea.Click();
                string js = @"arguments[0].value += arguments[1];arguments[0].dispatchEvent(new Event('input', { bubbles:true }));";
                ((IJavaScriptExecutor)d).ExecuteScript(js, textArea, message);
                var button = modal.FindElement(By.CssSelector("div[class*='index_answerWrapper_'] button[type='submit']"));
                try { button.Click(); }
                catch
                {
                    ((IJavaScriptExecutor)d).ExecuteScript("arguments[0].click();", button);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отправке сообщения через Selenium");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке модального окна");
        }

        return false;
    }

    public void SetArticleInSearchFieldCore(IWebDriver d, string article)
    {
        for (int i = 0; i < 3; i++)
        {
            try
            {
                Thread.Sleep(1000);
                var field = d.FindElement(By.CssSelector("input[id*=\"baseInput__\"]"));
                ((IJavaScriptExecutor)d).ExecuteScript("arguments[0].click();", field);
                var js = (IJavaScriptExecutor)d;
                js.ExecuteScript("arguments[0].value = '';", field);
                js.ExecuteScript("arguments[0].dispatchEvent(new Event('input', {bubbles:true}));", field);
                ((IJavaScriptExecutor)d).ExecuteScript("arguments[0].value = arguments[1];", field, article);
                ((IJavaScriptExecutor)d).ExecuteScript("arguments[0].dispatchEvent(new Event('input', { bubbles: true }));", field);
                Thread.Sleep(1000);
                break;
            }
            catch
            {
                d.Navigate().Refresh();
                if (i == 2)
                {
                    _logger.LogError("Не удалось установить артикул в поле поиска после 3 попыток.");
                }
            }
        }
    }

    public bool RefreshActiveTopic(string requestId, string article)
    {
        return _gate.RunSync(d =>
        {
            try
            {
                var temp = requestId.Split('_');
                var findDate = DateOnly.ParseExact(temp[1], "dd.MM.yyyy", CultureInfo.InvariantCulture);
                var findTime = TimeOnly.ParseExact(temp[2], "HH:mm");
                var findClientName = temp[3];

                ClickCalendarDateCore(d, findDate).GetAwaiter().GetResult();
                Thread.Sleep(300);
                SetArticleInSearchFieldCore(d, article);
                Thread.Sleep(300);
                int totalRows = ScrollContainerToEnd(d, "tbody tr[class*='index_row_']");
                var rows = d.FindElements(By.CssSelector("tbody tr[class*='index_row_']")).ToList();
                if (rows.Count < totalRows)
                {
                    rows = d.FindElements(By.CssSelector("tbody tr[class*='index_row_']")).ToList();
                }

                foreach (var row in rows)
                {
                    try
                    {
                        var cells = row.FindElements(By.TagName("td"));
                        if (cells.Count < 11)
                            continue;

                        var dateTimeDto = ParseDateTime(cells[10].Text);
                        if (dateTimeDto == null)
                        {
                            Thread.Sleep(300);
                            dateTimeDto = ParseDateTime(cells[10].Text);
                            if (dateTimeDto == null)
                                continue;
                        }
                        else if (dateTimeDto.Date != findDate || dateTimeDto.Time != findTime)
                        {
                            if (dateTimeDto.Date < findDate)
                                return true;
                            continue;
                        }

                        var processModalFromReviewsParserDto = new ProcessModalFromReviewsParserDto { ReviewText = "", Dialog = "" };
                        try
                        {
                            var reviewButton = cells[4];
                            if (!string.IsNullOrEmpty(reviewButton.Text))
                            {
                                if (reviewButton.Enabled)
                                    try { reviewButton.Click(); }
                                    catch
                                    {
                                        ((IJavaScriptExecutor)d).ExecuteScript("arguments[0].click();", reviewButton);
                                    }
                                else
                                    ((IJavaScriptExecutor)d).ExecuteScript("arguments[0].click();", reviewButton);

                                var tmp = ProcessModalToRefreshActiveTopic(d, findClientName, out bool refresh);
                                if (tmp)
                                    return refresh;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Ошибка при обработке модального окна");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при обработке строки");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ошибка в RefreshActiveTopic");
            }
            return false;
        });
    }

    private bool ProcessModalToRefreshActiveTopic(IWebDriver d, string clientName, out bool refreshTopic)
    {
        refreshTopic = false;
        string reviewText = string.Empty;
        string dialog = string.Empty;
        var shortWait = new WebDriverWait(d, TimeSpan.FromSeconds(3));

        var modal = _wait.Until(ExpectedConditions.ElementIsVisible(
            By.CssSelector("div[class*='drawer-module_drawer_'] > div[class*='index_container_']")));

        if (modal == null)
        {
            _logger.LogWarning("Модальное окно не найдено.");
        }

        Task.Delay(100).Wait();

        var findClientName = modal!
            .FindElement(By.CssSelector("div[class*='index_reviewInfo_'] > div:nth-child(2) div[class*='index_content_'] > div"))
            .Text.Trim();

        if (findClientName != clientName)
        {
            d.FindElement(By.TagName("body")).SendKeys(Keys.Escape);
            return false;
        }

        try
        {
            var reviewNode = _wait.Until(drv =>
            {
                try
                {
                    var infoNodes = modal.FindElements(
                        By.CssSelector("div[class*='index_reviewInfo_'] > div[class*='index_info_']"));
                    if (infoNodes.Count > 2)
                    {
                        var candidate = infoNodes[1]
                            .FindElements(By.CssSelector("div[class*='index_content_'] > div"))
                            .FirstOrDefault();
                        return candidate;
                    }
                }
                catch (StaleElementReferenceException) { }
                return null;
            });

            if (reviewNode != null)
            {
                reviewText = reviewNode.Text.Trim();
            }
            else
            {
                _logger.LogWarning("Не удалось найти узел с текстом отзыва.");
            }
        }
        catch (WebDriverTimeoutException ex)
        {
            _logger.LogWarning(ex, "Таймаут при ожидании узла с отзывом.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении отзыва через Selenium.");
        }

        // Сбор диалога
        try
        {
            var comments = modal.FindElements(
                By.CssSelector("div[class*='index_comments_'] div[class*='index_ownerComment_']"));
            var list = new List<string>();
            foreach (var comment in comments)
            {
                refreshTopic = false;
                var user = comment
                    .FindElements(By.CssSelector("div[class*='index_header_'] div[class*='index_title_']"))
                    .FirstOrDefault()?.Text.Trim() ?? "";
                var text = comment
                    .FindElements(By.CssSelector("div[class*='index_commentText_']"))
                    .FirstOrDefault()?.Text.Trim() ?? "";

                if (!string.IsNullOrEmpty(user) || !string.IsNullOrEmpty(text))
                {
                    list.Add($"{user}: {text}");
                    if (user.Contains(_sellerName)) refreshTopic = true;
                }
            }
            dialog = string.Join("\n", list);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при извлечении комментариев.");
        }

        Task.Delay(200).Wait();

        // Закрытие модального окна
        try
        {
            //_logger.LogInformation("Закрываю модальное окно нажатием ESC.");
            d.FindElement(By.TagName("body")).SendKeys(Keys.Escape);
        }
        catch (Exception ex) when (ex is NoSuchElementException || ex is WebDriverException)
        {
            _logger.LogWarning(ex, "Не удалось отправить ESC для закрытия модального окна.");
        }

        try
        {
            shortWait.Until(drv =>
                drv.FindElements(By.CssSelector("div[class*='drawer-module_drawer_'] > div[class*='index_container_']"))
                   .Count == 0);
            //_logger.LogInformation("Модальное окно успешно закрыто.");
        }
        catch (WebDriverTimeoutException)
        {
            _logger.LogWarning("Модальное окно не исчезло вовремя после ESC.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при ожидании исчезновения модального окна.");
        }

        return true;
    }

    public void RefreshTopicsForQuestions(IEnumerable<string> requestIds, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public void ProcessedButtonClick()
    {
        _gate.RunSync(d =>
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    var button = d.FindElement(By.XPath("//*[@id=\"app\"]/main/div[1]/div[1]/div/main/div[2]/button[4]"));
                    try { button.Click(); }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Ошибка при клике на кнопку обработанных отзывов, пробую через JS");
                        ((IJavaScriptExecutor)d).ExecuteScript("arguments[0].click();", button);
                    }
                    return;
                }
                catch
                {
                    Thread.Sleep(1000);
                }
            }
        });
    }

    public Task<bool> ClickCalendarDate(DateOnly date)
    {
        bool result = _gate.RunSync(d => ClickCalendarDateCore(d, date).GetAwaiter().GetResult());
        return Task.FromResult(result);
    }

    public Task<bool> ClickCalendarDateCore(IWebDriver driver, DateOnly date)
    {
        try
        {
            driver.Navigate().Refresh();
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));

            var chip = wait.Until(driver =>
            {
                // получаем все чип-кнопки
                var chips = driver.FindElements(
                    By.CssSelector("button[class*='filter-chip-module_filterChip_']"));

                // ждём, пока их станет ≥ 3 и сразу возвращаем нужную
                return chips.Count >= 2 ? chips[1] : null;   // null → пока условие не выполнено
            });


            try { chip.Click(); }
            catch (ElementClickInterceptedException)
            {
                // если обычный клик не сработал, пробуем через JavaScript
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", chip);
            }

            // ждём появления контейнера календаря
            wait.Until(ExpectedConditions.ElementIsVisible(
                By.CssSelector("div[class*='calendar-module_calendar_']")));

            string targetLabel = date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

            // 2. Листаем, пока не найдём нужный день
            for (var i = 0; i < 24; i++)        // 2 года запаса, чтобы не зациклиться
            {
                var targetBtn = driver.FindElements(
                        By.CssSelector($"button[data-testid='calendar-day'][aria-label='{targetLabel}']"))
                    .FirstOrDefault();

                if (targetBtn != null)
                {
                    wait.Until(ExpectedConditions.ElementToBeClickable(targetBtn));
                    try { targetBtn.Click(); }
                    catch (ElementClickInterceptedException)
                    {
                        // если обычный клик не сработал, пробуем через JavaScript
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", targetBtn);
                    }

                    wait.Until(ExpectedConditions.ElementToBeClickable(targetBtn));
                    try { targetBtn.Click(); }
                    catch (ElementClickInterceptedException)
                    {
                        // если обычный клик не сработал, пробуем через JavaScript
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", targetBtn);
                    }

                    Thread.Sleep(2000);
                    return Task.FromResult(true);                // успех
                }

                // определяем, какой месяц сейчас в левом календаре
                var monthName = driver
                    .FindElement(By.CssSelector("div[data-testid='calendar-month-name']"))
                    .Text
                    .Trim()
                    .ToLowerInvariant();

                var yearText = driver
                    .FindElement(By.CssSelector("div[data-testid='calendar-year-name']"))
                    .Text
                    .Trim();

                var currentMonth =
                    DateOnly.ParseExact($"01.{MonthNumber(monthName):D2}.{yearText}",
                                        "dd.MM.yyyy",
                                        CultureInfo.InvariantCulture);

                // если целевая дата позже ‒ жмём «вправо», иначе «влево»
                var arrowSelector = date > currentMonth
                    ? "svg[data-testid='calendar-arrow-right']"
                    : "svg[data-testid='calendar-arrow-left']";

                try { driver.FindElement(By.CssSelector(arrowSelector)).Click(); }
                catch (ElementClickInterceptedException)
                {
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();",
                                                                driver.FindElement(By.CssSelector(arrowSelector)));
                }
                wait.Until(d => d.FindElements(By.CssSelector($"button[data-testid='calendar-day'][aria-label='{targetLabel}']")).Count != 0 || d.FindElements(By.CssSelector(arrowSelector)).Count != 0);
            }

            throw new InvalidOperationException($"Не удалось найти дату {targetLabel} в календаре.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при выборе даты.");
            return Task.FromResult(false);
        }
    }

    /// <summary>Преобразует русское название месяца («май», «июнь», …) в номер 1-12.</summary>
    private static int MonthNumber(string ruMonth)
    {
        return DateTime.ParseExact(ruMonth, "MMMM", new CultureInfo("ru-RU")).Month;
    }
}
