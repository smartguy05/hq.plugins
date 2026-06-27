using System.Text.Json;
using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.LinkedIn.Models;
using Microsoft.Playwright;

namespace HQ.Plugins.LinkedIn;

/// <summary>
/// Drives a single authenticated LinkedIn session through a <b>persistent</b> Chromium
/// profile (<c>LaunchPersistentContextAsync</c>). The persistent user-data-dir retains the
/// httpOnly <c>li_at</c> auth cookie across restarts, which is why we use a profile rather
/// than manual cookie injection — JS cannot read or restore <c>li_at</c>. The password is
/// never handled here: the session is established once via the interactive login window
/// (<see cref="LinkedInLoginSession"/>) and reused thereafter.
/// </summary>
public sealed class LinkedInBrowser : ILinkedInBrowser
{
    public const string Origin = "https://www.linkedin.com";

    private readonly ServiceConfig _config;
    private readonly LogDelegate _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _displayOverride;
    private readonly bool _forceHeaded;

    private IPlaywright _playwright;
    private IBrowserContext _context;
    private IPage _page;

    public LinkedInBrowser(ServiceConfig config, LogDelegate log = null, string displayOverride = null, bool forceHeaded = false)
    {
        _config = config;
        _log = log;
        _displayOverride = displayOverride;
        _forceHeaded = forceHeaded;
    }

    /// <summary>
    /// Builds the persistent-context launch options shared by steady-state use and the
    /// interactive login. Anti-detection knobs (UA, locale, timezone) are kept stable so the
    /// profile presents a consistent fingerprint and builds a trust trail. Headed by default.
    /// </summary>
    public static BrowserTypeLaunchPersistentContextOptions BuildContextOptions(
        ServiceConfig config, bool headless, string display = null)
    {
        var options = new BrowserTypeLaunchPersistentContextOptions
        {
            Headless = headless,
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            Locale = string.IsNullOrWhiteSpace(config.Locale) ? "en-US" : config.Locale
        };

        if (!string.IsNullOrWhiteSpace(config.UserAgent)) options.UserAgent = config.UserAgent;
        if (!string.IsNullOrWhiteSpace(config.TimezoneId)) options.TimezoneId = config.TimezoneId;

        // Drop the "AutomationControlled" blink feature that exposes navigator.webdriver.
        options.Args = new[] { "--disable-blink-features=AutomationControlled" };

        if (!string.IsNullOrWhiteSpace(display))
            options.Env = new Dictionary<string, string> { { "DISPLAY", display } };

        return options;
    }

    private async Task EnsurePageAsync()
    {
        if (_page is not null) return;

        var profileDir = LinkedInPaths.ProfileDir(_config.AccountLabel);
        Directory.CreateDirectory(profileDir);

        _playwright = await Playwright.CreateAsync();
        var headless = _forceHeaded ? false : _config.Headless;
        var options = BuildContextOptions(_config, headless, _displayOverride);
        _context = await _playwright.Chromium.LaunchPersistentContextAsync(profileDir, options);
        _page = _context.Pages.Count > 0 ? _context.Pages[0] : await _context.NewPageAsync();
        _page.SetDefaultTimeout(30000);
    }

    public async Task<bool> IsAuthenticatedAsync()
    {
        var res = await VoyagerAsync("GET", "/voyager/api/me");
        return res.IsSuccess && res.RawBody.Contains("\"plainId\"", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<VoyagerResponse> VoyagerAsync(string method, string path, object body = null)
    {
        await _gate.WaitAsync();
        try
        {
            await EnsurePageAsync();

            // Ensure we're on the LinkedIn origin so document.cookie / fetch carry the session.
            if (!(_page.Url ?? "").StartsWith(Origin, StringComparison.OrdinalIgnoreCase))
                await _page.GotoAsync($"{Origin}/feed/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

            var url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : Origin + path;
            var bodyJson = body is null ? null : JsonSerializer.Serialize(body);

            var raw = await _page.EvaluateAsync<string>(FetchScript, new
            {
                url,
                method = method.ToUpperInvariant(),
                body = bodyJson
            });

            using var doc = JsonDocument.Parse(raw);
            var status = doc.RootElement.GetProperty("status").GetInt32();
            var respBody = doc.RootElement.GetProperty("body").GetString() ?? "";
            return new VoyagerResponse(status, respBody);
        }
        catch (Exception ex)
        {
            _log?.Invoke(LogLevel.Error, $"LinkedIn Voyager {method} {path} failed: {ex.Message}");
            return new VoyagerResponse(0, "");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// In-page fetch wrapper. Runs in the linkedin.com origin so cookies (including the
    /// httpOnly <c>li_at</c>) are attached automatically; we only need to echo the CSRF token,
    /// which LinkedIn derives from the <c>JSESSIONID</c> cookie.
    /// </summary>
    private const string FetchScript = @"
async (args) => {
  const csrf = ((document.cookie.match(/JSESSIONID=""?([^"";]+)""?/) || [])[1]) || '';
  const headers = {
    'csrf-token': csrf,
    'accept': 'application/vnd.linkedin.normalized+json+2.1',
    'x-restli-protocol-version': '2.0.0',
    'x-li-lang': 'en_US'
  };
  const init = { method: args.method, headers, credentials: 'include' };
  if (args.body) { headers['content-type'] = 'application/json; charset=UTF-8'; init.body = args.body; }
  let status = 0, body = '';
  try {
    const res = await fetch(args.url, init);
    status = res.status;
    body = await res.text();
  } catch (e) {
    body = String(e);
  }
  return JSON.stringify({ status, body });
}";

    /// <summary>
    /// Pure helper: extracts the CSRF token LinkedIn expects (the <c>JSESSIONID</c> value with
    /// any surrounding quotes stripped) from a raw Cookie header string. Exposed for testing.
    /// </summary>
    public static string CsrfFromCookie(string cookieHeader)
    {
        if (string.IsNullOrWhiteSpace(cookieHeader)) return "";
        foreach (var part in cookieHeader.Split(';'))
        {
            var kv = part.Trim();
            const string key = "JSESSIONID=";
            if (kv.StartsWith(key, StringComparison.Ordinal))
                return kv[key.Length..].Trim('"');
        }
        return "";
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_context is not null) await _context.CloseAsync();
            _context = null;
            _page = null;
            _playwright?.Dispose();
            _playwright = null;
        }
        catch { /* best-effort teardown */ }
        finally
        {
            _gate.Release();
        }
    }
}
