using Bl.Common.DTOs;
using Bl.Interfaces;
using DAL.Models;
using DocumentFormat.OpenXml.Bibliography;
using DocumentFormat.OpenXml.Spreadsheet;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Collections.ObjectModel;

namespace Bl.Implementations
{
    public class ChatParserService : IChatParserService
    {
        private readonly int _clickAttempts = 3;
        private readonly IChatParserBl _chatParserBl;
        private readonly SeleniumGate _gate;

        public ChatParserService(IChatParserBl chatParserBl, SeleniumGate gate)
        {
            _chatParserBl = chatParserBl;
            _gate = gate;
        }

        public void Navigate(string url, int maxAttempts = 3, int waitSeconds = 30)
        {
            _gate.RunAsync(async d =>
            {
                int attempt = 0;
                bool isLoaded = false;
                while (attempt < maxAttempts && !isLoaded)
                {
                    try
                    {
                        d.Navigate().GoToUrl(url);
                        var wait = new WebDriverWait(d, TimeSpan.FromSeconds(waitSeconds));
                        await Task.Run(() =>
                            wait.Until(driver =>
                                ((IJavaScriptExecutor)driver)
                                    .ExecuteScript("return document.readyState")
                                    .ToString()
                                    .Equals("complete", StringComparison.InvariantCultureIgnoreCase)
                        ));
                        isLoaded = true;
                    }
                    catch
                    {
                        attempt++;
                        if (attempt >= maxAttempts) throw;
                        await Task.Delay(2000);
                    }
                }
            }).GetAwaiter().GetResult();
        }

        private HashSet<string> ExtractAllChatsCore(IWebDriver d)
        {
            var container = d.FindElement(By.XPath("//*[@id=\"app\"]/main/div[1]/div[1]/div/div/div[1]/div[1]/div[4]/div"));
            int prevCount = container.FindElements(By.CssSelector("div[class*=\"index_chat_\"]")).Count;
            while (true)
            {
                ((IJavaScriptExecutor)d).ExecuteScript("arguments[0].scrollTop = arguments[0].scrollHeight;", container);
                Thread.Sleep(TimeSpan.FromSeconds(2));
                int currCount = container.FindElements(By.CssSelector("div[class*=\"index_chat_\"]")).Count;
                if (currCount <= prevCount) break;
                prevCount = currCount;
            }

            var chats = container.FindElements(By.CssSelector("div[class*=\"index_chat_\"]")).ToList();
            var data = new List<ChatRecord>();

            foreach (var chat in chats)
            {
                string title = chat.GetAttribute("title") ?? "";
                string unread = chat.GetAttribute("unreadcount") ?? "";
                var dateStr = chat.FindElement(By.CssSelector("span[class*=\"index_chatDate_\"]")).Text.Trim();
                DateOnly date = ParseDate(dateStr);
                string chatId = ExtractChatId(chat);
                string preview = "";
                try { preview = chat.FindElement(By.CssSelector("span[class*=\"preview_preview_\"]")).Text.Trim(); } catch { }
                TryClickElement(chat);
                IWebElement msgContainer = WaitForElement(d, "div.om_1_e5", 10);
                string history = msgContainer != null ? msgContainer.Text : "";
                data.Add(new ChatRecord
                {
                    Title = title,
                    Unread = unread,
                    Date = date,
                    ChatId = chatId,
                    Preview = preview,
                    History = history
                });
            }

            _chatParserBl.AddOrUpdateChats(data);
            Console.WriteLine("Полная загрузка завершена.");
            return data.Select(x => x.ChatId).ToHashSet();
        }
        public HashSet<string> ExtractAllChats()
        {
            return _gate.RunAsync<HashSet<string>>(async d =>
            {
                return ExtractAllChatsCore(d);
            }).GetAwaiter().GetResult();
        }

        public HashSet<string> ExtractNewChats(HashSet<string> chatIds, DateOnly? minDate = null)
        {
            return _gate.RunAsync(async d =>
            {
                if (chatIds.Count == 0)
                {
                    return ExtractAllChatsCore(d);
                }

                var container = WaitForElementXPath(d, "//*[@id=\"app\"]/main/div[1]/div[1]/div/div/div[1]/div[1]/div[4]/div", 10);
                var result = new List<InTopicModelDto<ChatRecord>>();
                var processedChatIds = new HashSet<string>();
                bool reachedOldChat = false;
                int newlyAddedChats = 0;

                while (true)
                {
                    newlyAddedChats = 0;
                    ReadOnlyCollection<IWebElement> allChats = new ReadOnlyCollection<IWebElement>([]);
                    for (int u = 0; u < 3; u++)
                    {
                        try
                        {
                            allChats = container
                                .FindElements(By.CssSelector("div[class*='index_chat_']"));
                        }
                        catch
                        {
                            await Task.Delay(500);
                        }
                    }
                    foreach (var chat in allChats)
                    {
                        bool inTopic = true;
                        string title = chat.GetAttribute("title") ?? "";
                        string unread = chat.GetAttribute("unreadcount") ?? "";
                        string rawDate = "";
                        try
                        {
                            rawDate = chat
                                .FindElement(By.CssSelector("span[class*=\"index_chatDate_\"]"))
                                .Text.Trim();
                        }
                        catch
                        {
                            continue;
                        }

                        var parsedDate = ParseDate(rawDate);

                        var chatId = ExtractChatId(chat);

                        if (chatIds.Contains(chatId))
                        {
                            reachedOldChat = true;
                            break;
                        }

                        if (!processedChatIds.Add(chatId))
                        {
                            continue;
                        }

                        TryClickElement(chat);
                        await Task.Delay(400);
                        var msgContainer = WaitForElementXPath(d, "//*[@id=\"app\"]/main/div[1]/div[1]/div/div/div[1]/div[2]/div[2]/div[1]", 10);
                        await Task.Delay(400);
                        var lastMessage = msgContainer.FindElements(By.CssSelector("div")).Last();
                        await Task.Delay(400);
                        var cls = lastMessage.GetAttribute("class").Split().Length > 2;

                        if (cls)
                            inTopic = false;

                        string history = msgContainer?.Text ?? "";

                        if (!minDate.HasValue || parsedDate >= minDate.Value)
                        {
                            result.Add(new InTopicModelDto<ChatRecord>
                            {
                                Model = new ChatRecord
                                {
                                    Title = title,
                                    Unread = unread,
                                    Date = parsedDate,
                                    ChatId = chatId,
                                    Preview = chat.FindElement(By.CssSelector("span[class*=\"preview_preview_\"]")).Text.Trim(),
                                    History = history
                                },
                                InTopic = inTopic
                            });
                        }

                        newlyAddedChats++;
                    }

                    if (reachedOldChat)
                    {
                        break;
                    }

                    if (newlyAddedChats == 0)
                    {
                        break;
                    }

                    ((IJavaScriptExecutor)d)
                        .ExecuteScript("arguments[0].scrollTop += 500;", container);
                    await Task.Delay(1500);
                }

                var filtered = !minDate.HasValue ? result.Select(x => x.Model) : result.Where(r => r.Model.Date >= minDate.Value).Select(x => x.Model);
                _chatParserBl.AddOrUpdateChats(filtered.ToList());
                Console.WriteLine("Инкрементальное обновление завершено.");
                return result.Where(y => y.InTopic == true).Select(x => x.Model.ChatId).ToHashSet();
            }).GetAwaiter().GetResult();
        }
        public HashSet<string> UpdateChats(DateOnly? minDate = null)
        {
            return _gate.RunAsync(async d =>
            {
                try
                {
                    Retry(() =>
                    {
                        var btn = d.FindElement(By.XPath("//button[.//span[text()='Только новые']]"));
                        TryClickElement(btn);
                    }, 3, 1000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Ошибка при клике 'Только новые': " + ex.Message);
                }

                await Task.Delay(1000);

                var chats = d.FindElements(By.CssSelector("div[class*=\"index_chat_\"]")).ToList();

                var result = new List<InTopicModelDto<ChatRecord>>();
                foreach (var chat in chats)
                {
                    bool inTopic = true;
                    string title = chat.GetAttribute("title") ?? "";
                    var dateStr = chat.FindElement(By.CssSelector("span[class*=\"index_chatDate_\"]")).Text.Trim();
                    DateOnly date = ParseDate(dateStr);
                    string chatId = ExtractChatId(chat);

                    Retry(() =>
                    {
                        TryClickElement(chat);
                    }, 3, 500);


                    await Task.Delay(400);
                    var msgContainer = WaitForElementXPath(d, "//*[@id=\"app\"]/main/div[1]/div[1]/div/div/div[1]/div[2]/div[2]/div[1]", 10);
                    var lastMessage = msgContainer.FindElements(By.CssSelector("div")).Last();

                    var cls = lastMessage.GetAttribute("class").Split().Length > 2;

                    if (cls)
                        inTopic = false;

                    string history = msgContainer?.Text ?? "";

                    if (!minDate.HasValue || date >= minDate.Value)
                    {
                        result.Add(new InTopicModelDto<ChatRecord>
                        {
                            Model = new ChatRecord
                            {
                                Title = title,
                                Date = date,
                                ChatId = chatId,
                                History = history
                            },
                            InTopic = inTopic
                        });
                    }
                }

                var filtered = !minDate.HasValue ? result.Select(x => x.Model) : result.Where(r => r.Model.Date >= minDate.Value).Select(x => x.Model);
                _chatParserBl.AddOrUpdateChats(filtered.ToList());

                Console.WriteLine("Обновление диалогов завершено.");
                return result.Where(y => y.InTopic == true).Select(x => x.Model.ChatId).ToHashSet();
            }).GetAwaiter().GetResult();
        }

        private bool TryClickElement(IWebElement element)
        {
            for (int i = 0; i < _clickAttempts; i++)
            {
                try { element.Click(); return true; }
                catch (Exception ex)
                {
                    Console.WriteLine($"Попытка {i + 1} клика не удалась: {ex.Message}");
                    Thread.Sleep(200);
                }
            }
            return false;
        }

        private IWebElement WaitForElement(IWebDriver driver, string cssSelector, int maxAttempts)
        {
            IWebElement element = null;
            int attempts = 0;
            while (attempts < maxAttempts)
            {
                try
                {
                    element = driver.FindElement(By.CssSelector(cssSelector));
                    if (element.Displayed && element.Enabled) return element;
                }
                catch { }
                Thread.Sleep(200);
                attempts++;
            }
            return element;
        }

        private IWebElement WaitForElementXPath(IWebDriver driver, string cssSelector, int maxAttempts)
        {
            IWebElement element = null;
            int attempts = 0;
            while (attempts < maxAttempts)
            {
                try
                {
                    element = driver.FindElement(By.XPath(cssSelector));
                    if (element.Displayed && element.Enabled) return element;
                }
                catch { }
                Thread.Sleep(200);
                attempts++;
            }
            return element;
        }

        private IWebElement WaitForElement(IWebDriver driver, IWebElement webElement, string cssSelector, int maxAttempts)
        {
            IWebElement element = null;
            int attempts = 0;
            while (attempts < maxAttempts)
            {
                try
                {
                    element = webElement.FindElement(By.CssSelector(cssSelector));
                    if (element.Displayed && element.Enabled) return element;
                }
                catch { }
                Thread.Sleep(200);
                attempts++;
            }
            return element;
        }

        private IWebElement WaitForElementTagType(IWebDriver driver, IWebElement webElement, string tagName, int maxAttempts)
        {
            IWebElement element = null;
            int attempts = 0;
            while (attempts < maxAttempts)
            {
                try
                {
                    element = webElement.FindElement(By.TagName(tagName));
                    if (element.Displayed && element.Enabled) return element;
                }
                catch { }
                Thread.Sleep(200);
                attempts++;
            }
            return element;
        }

        private static DateOnly ParseDate(string dateText)
        {
            if (string.IsNullOrWhiteSpace(dateText) || dateText.Contains(':'))
                return DateOnly.FromDateTime(DateTime.UtcNow);

            int dotCount = dateText.Count(c => c == '.');

            if (dotCount == 1)
            {
                DateTime dt = DateTime.ParseExact(dateText + "." + DateTime.Today.Year, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
                return DateOnly.FromDateTime(dt);
            }
            else if (dotCount == 2)
            {
                DateOnly dt = DateOnly.ParseExact(dateText, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture);
                return dt;
            }

            return DateOnly.FromDateTime(DateTime.UtcNow);
        }

        private static string ExtractChatId(IWebElement chat)
        {
            try
            {
                string deeplink = chat.GetAttribute("deeplink") ?? "";
                int idIndex = deeplink.IndexOf("id=");
                if (idIndex != -1)
                {
                    int start = idIndex + 3;
                    int end = deeplink.IndexOf('&', start);
                    if (end == -1)
                        return deeplink.Substring(start);
                    else
                        return deeplink.Substring(start, end - start);
                }
            }
            catch { }
            return "";
        }

        public bool SendMessageToClient(string requestId, string message)
        {
            return _gate.RunAsync(async d =>
            {
                var chatModel = _chatParserBl.GetChatRecordByChatId(requestId);
                WebDriverWait wait = new WebDriverWait(d, TimeSpan.FromSeconds(10));

                if (chatModel == null)
                {
                    return false;
                }

                var container = WaitForElementXPath(d, "//*[@id=\"app\"]/main/div[1]/div[1]/div/div/div[1]/div[1]/div[4]/div", 10);

                while (true)
                {
                    var allChats = container
                        .FindElements(By.CssSelector("div[class*=\"index_chat_\"]"))
                        .ToList();

                    foreach (var chat in allChats)
                    {
                        var chatId = ExtractChatId(chat);

                        if (chatId == chatModel.ChatId)
                        {
                            TryClickElement(chat);
                            await Task.Delay(400);
                            var msgContainer = WaitForElementXPath(d, "//*[@id=\"app\"]/main/div[1]/div[1]/div/div/div[1]/div[2]/div[4]", 10);
                            await Task.Delay(400);
                            var textArea = WaitForElementTagType(d, msgContainer, "textarea", 10);
                            await Task.Delay(400);
                            textArea.Click();
                            string js = @"arguments[0].value += arguments[1];arguments[0].dispatchEvent(new Event('input', { bubbles:true }));";
                            ((IJavaScriptExecutor)d).ExecuteScript(js, textArea, message);
                            //textArea.SendKeys(message);
                            var panel = d.FindElement(By.XPath("//*[@id=\"app\"]/main/div[1]/div[1]/div/div/div[1]/div[2]/div[4]"));
                            var sendBtn = panel.FindElements(By.TagName("button"))[1];

                            sendBtn.Click();
                            return true;
                        }

                    }

                    ((IJavaScriptExecutor)d)
                        .ExecuteScript("arguments[0].scrollTop += 500;", container);
                    await Task.Delay(1000);
                }
            }).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Выполняет переданное действие с повторными попытками в случае ошибки.
        /// </summary>
        /// <param name="action">Действие, которое нужно выполнить.</param>
        /// <param name="maxAttempts">Максимальное число попыток.</param>
        /// <param name="delayMs">Задержка между попытками в миллисекундах.</param>
        private void Retry(Action action, int maxAttempts, int delayMs)
        {
            int attempts = 0;
            while (true)
            {
                try
                {
                    action();
                    break;
                }
                catch
                {
                    attempts++;
                    if (attempts >= maxAttempts)
                        throw;
                    Thread.Sleep(delayMs);
                }
            }
        }

        public bool RefreshActiveTopic(string requestId)
        {
            return _gate.RunAsync(async d =>
            {
                var chatModel = _chatParserBl.GetChatRecordByChatId(requestId);
                WebDriverWait wait = new WebDriverWait(d, TimeSpan.FromSeconds(10));

                if (chatModel == null)  
                {
                    return false;
                }

                var container = WaitForElementXPath(d, "//*[@id=\"app\"]/main/div[1]/div[1]/div/div/div[1]/div[1]/div[4]/div", 10);

                while (true)
                {
                    var allChats = d
                        .FindElements(By.CssSelector("div[class*=\"index_chat_\"]"))
                        .ToList();

                    foreach (var chat in allChats)
                    {
                        var chatId = ExtractChatId(chat);

                        if (chatId == chatModel.ChatId)
                        {
                            TryClickElement(chat);
                            await Task.Delay(400);
                            var msgContainer = WaitForElementXPath(d, "//*[@id=\"app\"]/main/div[1]/div[1]/div/div/div[1]/div[2]/div[2]/div[1]", 10);
                            await Task.Delay(400);
                            var lastMessage = msgContainer.FindElements(By.CssSelector("div")).Last();
                            await Task.Delay(400);

                            var cls = lastMessage.GetAttribute("class").Split().Length > 2;

                            if (cls)
                                return true;

                            return false;
                        }

                    }

                    for (int i = 0; i < 3; i++)
                    {
                        try
                        {
                            ((IJavaScriptExecutor)d)
                                .ExecuteScript("arguments[0].scrollTop += 500;", container);
                            break;
                        }
                        catch
                        {
                            if (i == 2)
                                Console.WriteLine("ПРИ СКРОЛЛЕ ЧАТОВ ВОЗНИКЛА ОШИБКА");
                            await Task.Delay(500);
                            container = WaitForElementXPath(d, "//*[@id=\"app\"]/main/div[1]/div[1]/div/div/div[1]/div[1]/div[4]/div", 10);
                        }
                    }
                    await Task.Delay(1000);
                }
            }).GetAwaiter().GetResult();
        }
    }
}
