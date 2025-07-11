using OpenQA.Selenium;

namespace Bl.Gates;

public sealed class SeleniumGate
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IWebDriver _driver;

    public SeleniumGate(IWebDriver driver) => _driver = driver;

    public async Task<T> RunAsync<T>(Func<IWebDriver, Task<T>> action,
                                     CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await action(_driver).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }
    public async Task RunAsync(Func<IWebDriver, Task> action,
                               CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { await action(_driver); }
        finally { _lock.Release(); }
    }
    public T RunSync<T>(Func<IWebDriver, T> action, CancellationToken ct = default)
    {
        _lock.Wait(ct);
        try
        {
            return action(_driver);
        }
        finally
        {
            _lock.Release();
        }
    }
    public void RunSync(Action<IWebDriver> action, CancellationToken ct = default)
    {
        _lock.Wait(ct);
        try
        {
            action(_driver);
        }
        finally
        {
            _lock.Release();
        }
    }
}
