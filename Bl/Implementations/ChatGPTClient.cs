using Bl.Interfaces;
using Common.Configuration.Configs;
using Common.Enums;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bl.Implementations;

public sealed class ChatGPTClient : IChatGPTClient
{
    private readonly string _apiKey;
    private readonly HttpClient _httpClient;
    private readonly ChatGptConfig _config;
    private const string BaseUrl = "https://api.openai.com/v1/";

    private static readonly SemaphoreSlim _semaphore = new(initialCount: 2);

    private const double TokenCapacity = 120000;
    private const double TokensPerMinute = 30000;
    private const double TokensPerSecond = TokensPerMinute / 60.0;
    private static double _availableTokens = TokenCapacity;
    private static DateTime _lastTokenRefillTimestamp = DateTime.UtcNow;
    private static readonly object _tokenLock = new();

    private const int MaxRunRetries = 3;
    private const int RunTimeoutSeconds = 300;
    private const int BackoffBaseSeconds = 2;

    public ChatGPTClient(IOptions<ChatGptConfig> config, HttpClient? client = null)
    {
        _config = config.Value;
        _apiKey = _config.ApiKey;

        _httpClient = client ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        _httpClient.DefaultRequestHeaders.Add("OpenAI-Beta", "assistants=v2");
    }

    /// <summary>Пытается отменить run; возвращает true, если OpenAI подтвердил отмену (HTTP 200).</summary>
    private async Task<bool> TryCancelRunAsync(string threadId, string runId, CancellationToken ct)
    {
        var url = $"{BaseUrl}threads/{threadId}/runs/{runId}/cancel";
        var resp = await _httpClient.PostAsync(url, null, ct);
        return resp.IsSuccessStatusCode;   // 200 { "id": "...", "cancelled": true }
    }

    /// <summary>
    /// Получает список сообщений в thread. По умолчанию — до 100 последних
    /// (параметр order=asc оставляет их в хронологическом порядке).
    /// </summary>
    private async Task<List<Message>> ListMessagesAsync(
        string threadId,
        int limit = 100,
        CancellationToken ct = default)
    {
        var url = $"{BaseUrl}threads/{threadId}/messages?limit={limit}&order=asc";
        var resp = await _httpClient.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(ct);
        var list = JsonConvert.DeserializeObject<MessageList>(json);
        return list?.Data ?? [];             // если null — вернуть пустой список
    }

    /// <summary>
    /// Удаляет конкретное сообщение из thread.
    /// </summary>
    private async Task DeleteMessageAsync(
        string threadId,
        string messageId,
        CancellationToken ct = default)
    {
        var url = $"{BaseUrl}threads/{threadId}/messages/{messageId}";
        var resp = await _httpClient.DeleteAsync(url, ct);
        resp.EnsureSuccessStatusCode();         // ожидать 200 { "id": "...", "deleted": true }
    }

    private record Message(
string Id,
string Role,
List<MsgContent> Content,
long Created_At);

    private record MsgContent(
        string Type,
        MsgText Text);

    private record MsgText(string Value);

    private record MessageList(List<Message> Data);

    private static async Task AcquireTokensAsync(int tokens, CancellationToken ct)
    {
        if (tokens > (int)TokenCapacity)
            throw new ArgumentException(
            $"Message requires {tokens} tokens, which exceeds the hard limit of {TokenCapacity}. " +
            "Shorten the message or split it before sending.");

        while (true)
        {
            int waitMs = 0;
            lock (_tokenLock)
            {
                var now = DateTime.UtcNow;
                var elapsed = (now - _lastTokenRefillTimestamp).TotalSeconds;
                if (elapsed > 0)
                {
                    _availableTokens = Math.Min(TokenCapacity, _availableTokens + elapsed * TokensPerSecond);
                    _lastTokenRefillTimestamp = now;
                }

                if (_availableTokens >= tokens)
                {
                    _availableTokens -= tokens;
                    return;
                }

                var needed = tokens - _availableTokens;
                waitMs = (int)Math.Ceiling((needed / TokensPerSecond) * 1000);
            }

            // Wait for enough tokens to accumulate
            await Task.Delay(waitMs, ct);
        }
    }

    private static int CountTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        // Rough estimate: 1 token per 4 characters (approx)
        return (text.Length + 3) / 1;
    }
    private static int CountTokens(IEnumerable<Message> messages)
    {
        int total = 0;

        foreach (var msg in messages)
        {
            if (msg?.Content == null) continue;

            foreach (var part in msg.Content)
            {
                if (part.Type == "text" &&
                    part.Text != null &&
                    !string.IsNullOrEmpty(part.Text.Value))
                {
                    total += CountTokens(part.Text.Value);
                }
            }
        }
        return total;
    }



    #region Public API
    public async Task<string> SendMessageAsync(string userMessage, string assistantType, CancellationToken ct = default)
    {
        Console.WriteLine($"SendMessageAsync: send message: {userMessage} in gpt"); // TODO
        var (assistantId, threadId) = await GetAssistantAndThreadAsync(assistantType, ct);

        var correlationId = Guid.NewGuid().ToString("N");
        Console.WriteLine("SendMessageAsync: add user message");
        await AddUserMessageAsync(userMessage, threadId, correlationId, ct);
        Exception? lastError = null;

            for (int attempt = 0; attempt < MaxRunRetries; attempt++)
            {
                try
                {

                    Console.WriteLine($"SendMessageAsync: CreateRunAsync: attempt: {attempt}");
                    var run = await CreateRunAsync(assistantId, threadId, ct);
                    if (run.Status == "incomplete")
                    {
                        Console.WriteLine($"Run {run.Id} incomplete: {run.Incomplete_Details?.Reason ?? "unknown"}");
                        // При max_tokens попробуйте увеличить лимит, при content_filter — очистить/ослабить запрос
                    }
                    Console.WriteLine("SendMessageAsync: runner wait");
                    await WaitRunAsync(threadId, run.Id, ct, abortAfterS: RunTimeoutSeconds);
                    Console.WriteLine("SendMessageAsync: get answer");
                    var response = await GetAssistantResponseAsync(threadId, run.Id, ct);
                    return response;
                }
                catch (TimeoutException)
                {
                    // отменяем зависший run и готовимся к ретраю
                    _ = await TryCancelRunAsync(threadId, runId: "unknown", ct);  // runId можно передать из catch (см. ниже)
                    lastError = new TimeoutException($"Run timed-out after {RunTimeoutSeconds}s");
                }
                catch (Exception ex) when (IsRetriable(ex))
                {
                    lastError = ex;   // сохраним для возможного rethrow
                }

                // экспоненциальный back-off: 2, 4, 8 сек …
                var delay = TimeSpan.FromSeconds(Math.Pow(BackoffBaseSeconds, attempt + 1));
                await Task.Delay(delay, ct);
            }
            throw lastError ?? new Exception("Run failed after retries");
        }
    }
    private static bool IsRetriable(Exception ex)
        => ex is TimeoutException
           || ex.Message.Contains("rate_limit_exceeded")
           || ex.Message.Contains("temporarily_unavailable");
    #endregion

    #region Pipeline building blocks
    // Получить id ассистента и создать для него новый thread
    private async Task<(string assistantId, string threadId)> GetAssistantAndThreadAsync(string name, CancellationToken ct)
    {
        if (!_config.Assistants.TryGetValue(name, out var id))
            throw new ArgumentOutOfRangeException(nameof(name), name, "Assistant not configured");

        // создаём новый thread для запроса
        var url = $"{BaseUrl}threads";
        var resp = await _httpClient.PostAsync(url, null, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        var thread = JsonConvert.DeserializeObject<ThreadObj>(json) ?? throw new("Cannot deserialize thread");

        return (id, thread.Id);
    }

    private async Task AddUserMessageAsync(string text, string threadId, string correlationId, CancellationToken ct)
    {
        int tokenCount = CountTokens(text);
        await AcquireTokensAsync(tokenCount, ct);

        var url = $"{BaseUrl}threads/{threadId}/messages";
        var body = new
        {
            role = "user",
            content = text,
            metadata = new { correlationId }
        };
        var content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
        var resp = await _httpClient.PostAsync(url, content, ct);
        resp.EnsureSuccessStatusCode();
    }

    private async Task<Run> CreateRunAsync(string assistantId, string threadId, CancellationToken ct)
    {
        var msgs = await ListMessagesAsync(threadId, ct: ct);
        int maxPrompt = 100_000;
        foreach (var m in msgs.OrderBy(x => x.Created_At))
        {
            if (CountTokens(msgs) <= maxPrompt) break;
            await DeleteMessageAsync(threadId, m.Id, ct);   // DELETE /threads/{threadId}/messages/{m.Id}
            msgs.Remove(m);
        }
        await _semaphore.WaitAsync(ct);
        try
        {
            var instructions = "Пожалуйста, верни JSON с единственным полем response.";
            int instructionTokens = CountTokens(instructions);
            int trimmedMessages = CountTokens(msgs);
            var completionCap = 1024;
            await AcquireTokensAsync(instructionTokens + completionCap + trimmedMessages, ct);

            var url = $"{BaseUrl}threads/{threadId}/runs";
            var body = new
            {
                assistant_id = assistantId,
                additional_instructions = instructions,
                //max_prompt_tokens = null,
                max_completion_tokens = 1024
            };
            var content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");
            content.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

            var resp = await _httpClient.PostAsync(url, content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                throw new Exception(await resp.Content.ReadAsStringAsync(ct));
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            return JsonConvert.DeserializeObject<Run>(json) ?? throw new("Cannot deserialize Run");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task WaitRunAsync(string threadId, string runId, CancellationToken ct, int pollMs = 1_000, int abortAfterS = 300)
    {
        var url = $"{BaseUrl}threads/{threadId}/runs/{runId}";
        var sw = Stopwatch.StartNew();
        while (!ct.IsCancellationRequested)
        {
            var resp = await _httpClient.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            var run = JsonConvert.DeserializeObject<Run>(await resp.Content.ReadAsStringAsync(ct)) ?? throw new("Cannot deserialize Run");
            if (run.Status is "completed") return;
            if (IsFailureStatus(run.Status))
            {
                if (run.Status == "incomplete")
                {
                    // из incomplete_details.reason узнаём, что именно пошло не так
                    // варианты: max_prompt_tokens, max_tokens, content_filter, model_timeout …
                    var reason = run.Incomplete_Details?.Reason ?? "unknown";

                    throw new($"Run {runId} incomplete - {reason}");
                }
                else
                {
                    // сохраним ошибку в журнал, чтобы потом видеть первопричину
                    var reason = run.Last_Error != null
                        ? $"{run.Last_Error.Code}: {run.Last_Error.Message}"
                        : "no last_error returned";
                    throw new($"Run {runId} failed - {reason}");
                }
            }

            if (sw.Elapsed.TotalSeconds > abortAfterS)
            {
                await _httpClient.PostAsync($"{url}/cancel", null, ct);
                throw new TimeoutException($"Run {runId} stuck in {run.Status} > {abortAfterS}s");
            }
            await Task.Delay(pollMs, ct);
        }
        ct.ThrowIfCancellationRequested();
    }

    private async Task<string> GetAssistantResponseAsync(string threadId, string runId, CancellationToken ct)
    {
        var url = $"{BaseUrl}threads/{threadId}/messages";
        var resp = await _httpClient.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var root = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));

        var assistant = (root["data"]?
            .FirstOrDefault(m => m?["role"]?.ToString() == "assistant" &&
                                 m?["run_id"]?.ToString() == runId)) ?? throw new("Assistant message not found");
        var textToken = assistant["content"]?
            .FirstOrDefault(c => c?["type"]?.ToString() == "text")?["text"]?["value"];
        var text = textToken?.ToString() ?? string.Empty;
        return ExtractResponseField(text);
    }
    #endregion

    #region Helpers / DTOs
    private static bool IsTerminalStatus(string s) => s is "completed" or "failed" or "cancelled" or "expired" or "incomplete";
    private static bool IsFailureStatus(string s) => s is "failed" or "cancelled" or "expired" or "incomplete";

    private static string ExtractResponseField(string raw)
    {
        var fence = Regex.Match(raw, @"```json\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
        var jsonBlock = fence.Success ? fence.Groups[1].Value : raw;

        var l = jsonBlock.IndexOf('{');
        var r = jsonBlock.LastIndexOf('}');
        if (l < 0 || r < l) return raw;

        var clean = jsonBlock.Substring(l, r - l + 1);

        using var doc = JsonDocument.Parse(clean);
        var root = doc.RootElement;

        if (root.TryGetProperty("response", out var propLower) && propLower.ValueKind == JsonValueKind.String)
            return propLower.GetString()!;

        if (root.TryGetProperty("Response", out var propUpper) && propUpper.ValueKind == JsonValueKind.String)
            return propUpper.GetString()!;

        return clean;
    }


    private sealed class AsyncDisposable : IAsyncDisposable
    {
        private readonly Action _onDispose;
        public AsyncDisposable(Action onDispose) => _onDispose = onDispose;
        public ValueTask DisposeAsync()
        {
            _onDispose();
            return ValueTask.CompletedTask;
        }
    }

    private record Run(string Id,
               string Status,
               LastError? Last_Error,
               IncompleteDetails? Incomplete_Details);   // snake_case как в JSON

    private record IncompleteDetails(string Reason);

    private record LastError(string Code, string Message);
    private record RunList(List<Run> Data);
    private record ThreadObj(string Id);
    #endregion
}
