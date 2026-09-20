using HQ.Plugins.WebReader.Models;
using Microsoft.Playwright;

namespace HQ.Plugins.WebReader;

/// <summary>
/// Renders pages with a single shared headless Chromium instance. The browser is
/// launched lazily on first use and reused across requests; each request gets a
/// fresh context/page so cookies and state don't leak between reads.
/// Mirrors the launch options used by HQ.Plugins.HeadlessBrowser/BrowserClient.cs.
/// </summary>
public class PlaywrightRenderer : IPageRenderer, IAsyncDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly bool _headless;
    private readonly int _timeoutMs;
    private readonly string _userAgent;
    private readonly int _viewportWidth;
    private readonly int _viewportHeight;
    private readonly bool _waitForNetworkIdle;

    private IPlaywright _playwright;
    private IBrowser _browser;

    public PlaywrightRenderer(ServiceConfig config)
    {
        _headless = config.Headless;
        _timeoutMs = config.DefaultTimeoutMs;
        _userAgent = config.UserAgent;
        _viewportWidth = config.ViewportWidth;
        _viewportHeight = config.ViewportHeight;
        _waitForNetworkIdle = config.WaitForNetworkIdle;
    }

    public async Task<RenderedPage> RenderAsync(string url)
    {
        await _semaphore.WaitAsync();
        IBrowserContext context = null;
        try
        {
            await EnsureBrowserAsync();

            var contextOptions = new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = _viewportWidth, Height = _viewportHeight }
            };
            if (!string.IsNullOrWhiteSpace(_userAgent))
                contextOptions.UserAgent = _userAgent;

            context = await _browser.NewContextAsync(contextOptions);
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(_timeoutMs);
            await InstallUrlGuardRouteAsync(page);

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = _waitForNetworkIdle ? WaitUntilState.NetworkIdle : WaitUntilState.Load,
                Timeout = _timeoutMs
            });

            var html = await page.ContentAsync();
            var title = await page.TitleAsync();
            var finalUrl = page.Url;
            return new RenderedPage(html, finalUrl, title);
        }
        finally
        {
            if (context is not null)
                await context.CloseAsync();
            _semaphore.Release();
        }
    }

    private async Task EnsureBrowserAsync()
    {
        if (_browser is not null) return;

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(ChromiumLaunchOptions.Build(_headless));
    }

    /// <summary>
    /// WP6A-1 (Critical), defense in depth: <see cref="WebReaderCommand.WithRender"/> validates
    /// the requested URL before <see cref="RenderAsync"/> is ever called, but <c>GotoAsync</c>
    /// follows redirects and the rendered page's own script can issue requests that bypass that
    /// pre-check entirely. Aborting every request the guard rejects — installed once per page —
    /// closes both gaps without touching every call site.
    /// </summary>
    private static async Task InstallUrlGuardRouteAsync(IPage page)
    {
        await page.RouteAsync("**/*", async route =>
        {
            if (IsRequestNavigable(route.Request.Url))
                await route.ContinueAsync();
            else
                await route.AbortAsync();
        });
    }

    /// <summary>Pure predicate the route filter above consults. Exposed for testing without a real browser.</summary>
    internal static bool IsRequestNavigable(string url) => HQ.Models.Safety.UrlGuard.IsNavigable(url, out _);

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
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
