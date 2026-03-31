using HQ.Plugins.HeadlessBrowser.Pipeline;
using Microsoft.Playwright;

namespace HQ.Plugins.HeadlessBrowser;

public class BrowserClient : IBrowserClient
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
    private string _lastSnapshotUrl;

    public BrowserClient(Models.ServiceConfig config)
    {
        _headless = config.Headless;
        _timeoutMs = config.DefaultTimeoutMs;
        _userAgent = config.UserAgent;
        _viewportWidth = config.ViewportWidth;
        _viewportHeight = config.ViewportHeight;
    }

    public bool IsInitialized => _page is not null;
    public PageSnapshot CurrentSnapshot { get; private set; }
    public PageSnapshot PreviousSnapshot { get; private set; }

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

    public async Task<PageSnapshot> TakeSnapshotAsync(int timeoutMs = 10000)
    {
        await _semaphore.WaitAsync();
        try
        {
            await EnsureInitializedAsync();

            // Invalidate if URL changed
            if (_lastSnapshotUrl != _page.Url)
            {
                PreviousSnapshot = null;
                CurrentSnapshot = null;
            }

            var yaml = await AriaSnapshotExtractor.ExtractAsync(_page, timeoutMs);
            if (!AriaSnapshotExtractor.IsUsable(yaml))
                return null;

            PreviousSnapshot = CurrentSnapshot;
            CurrentSnapshot = RefAssigner.Assign(yaml, _page.Url);
            _lastSnapshotUrl = _page.Url;
            return CurrentSnapshot;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public ILocator ResolveRef(string refId)
    {
        if (_page is null || CurrentSnapshot is null)
            return null;

        if (!CurrentSnapshot.RefMap.TryGetValue(refId, out var elementRef))
            return null;

        // Resolve using Playwright's role-based locator for robustness
        if (!string.IsNullOrEmpty(elementRef.Name))
        {
            var role = ParseAriaRole(elementRef.Role);
            if (role.HasValue)
                return _page.GetByRole(role.Value, new PageGetByRoleOptions { Name = elementRef.Name, Exact = false });
        }

        // Fallback: use getByText if role parsing fails
        if (!string.IsNullOrEmpty(elementRef.Name))
            return _page.GetByText(elementRef.Name, new PageGetByTextOptions { Exact = false });

        return null;
    }

    private static AriaRole? ParseAriaRole(string role)
    {
        return role?.ToLowerInvariant() switch
        {
            "link" => AriaRole.Link,
            "button" => AriaRole.Button,
            "textbox" => AriaRole.Textbox,
            "combobox" => AriaRole.Combobox,
            "checkbox" => AriaRole.Checkbox,
            "radio" => AriaRole.Radio,
            "slider" => AriaRole.Slider,
            "spinbutton" => AriaRole.Spinbutton,
            "switch" => AriaRole.Switch,
            "menuitem" => AriaRole.Menuitem,
            "tab" => AriaRole.Tab,
            "option" => AriaRole.Option,
            "searchbox" => AriaRole.Searchbox,
            "treeitem" => AriaRole.Treeitem,
            "heading" => AriaRole.Heading,
            "navigation" => AriaRole.Navigation,
            "main" => AriaRole.Main,
            "form" => AriaRole.Form,
            _ => null
        };
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
