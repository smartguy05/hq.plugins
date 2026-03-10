using Microsoft.Playwright;

namespace HQ.Plugins.HeadlessBrowser;

public class BrowserClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private IPlaywright _playwright;
    private IBrowser _browser;
    private IPage _page;
    private readonly bool _headless;
    private readonly int _timeoutMs;
    private readonly string _userAgent;
    private readonly int _viewportWidth;
    private readonly int _viewportHeight;

    public BrowserClient(Models.ServiceConfig config)
    {
        _headless = config.Headless;
        _timeoutMs = config.DefaultTimeoutMs;
        _userAgent = config.UserAgent;
        _viewportWidth = config.ViewportWidth;
        _viewportHeight = config.ViewportHeight;
    }

    public bool IsInitialized => _page is not null;

    private async Task EnsureInitializedAsync()
    {
        if (_page is not null) return;

        _playwright = await Playwright.CreateAsync();

        var launchOptions = new BrowserTypeLaunchOptions
        {
            Headless = _headless
        };

        _browser = await _playwright.Chromium.LaunchAsync(launchOptions);

        var contextOptions = new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = _viewportWidth, Height = _viewportHeight }
        };

        if (!string.IsNullOrWhiteSpace(_userAgent))
            contextOptions.UserAgent = _userAgent;

        var context = await _browser.NewContextAsync(contextOptions);
        _page = await context.NewPageAsync();
        _page.SetDefaultTimeout(_timeoutMs);
    }

    public async Task<T> ExecuteAsync<T>(Func<IPage, Task<T>> action)
    {
        await _semaphore.WaitAsync();
        try
        {
            await EnsureInitializedAsync();
            return await action(_page);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ExecuteAsync(Func<IPage, Task> action)
    {
        await _semaphore.WaitAsync();
        try
        {
            await EnsureInitializedAsync();
            await action(_page);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_browser is not null)
            {
                await _browser.CloseAsync();
                _browser = null;
            }

            _playwright?.Dispose();
            _playwright = null;
            _page = null;
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
