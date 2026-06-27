using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.LinkedIn.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.Playwright;

namespace HQ.Plugins.LinkedIn;

/// <summary>
/// LinkedIn plugin driven by a self-hosted, authenticated browser session (no external
/// orchestration vendor). The user logs in once through the interactive login window
/// (<see cref="Endpoints.LinkedInLoginEndpoints"/>); the session is persisted in an on-disk
/// Chromium profile and reused by the agent's semantic tools. The password is never handled
/// by HQ or the model.
/// </summary>
public class LinkedInCommand :
    CommandBase<ServiceRequest, ServiceConfig>,
    IHasFrontend,
    IHasHttpRoutes
{
    public override string Name => "LinkedIn";
    public override string Description => "LinkedIn messaging, posting, profile lookup, and people/company search via a self-hosted authenticated browser session";
    protected override INotificationService NotificationService { get; set; }

    // Shared across requests in this process: one rate-limit gate, one cached browser per account.
    private static readonly RateLimitGate RateLimiter = new();
    private static readonly object BrowserLock = new();
    private static LinkedInBrowser _browser;
    private static string _browserAccount;

    /// <summary>Last config seen by the plugin — used by the login endpoints, which run outside agent context.</summary>
    internal static ServiceConfig LastConfig { get; private set; }

    public override List<ToolCall> GetToolDefinitions()
        => ServiceExtensions.GetServiceToolCalls<LinkedInService>();

    public override async Task<object> Initialize(string configString, LogDelegate logFunction, INotificationService notificationService)
    {
        await base.Initialize(configString, logFunction, notificationService);
        NotificationService ??= notificationService;

        try { LastConfig = configString.ReadPluginConfig<ServiceConfig>(); }
        catch { /* config may be absent at init */ }

        try
        {
            var exitCode = Program.Main(["install", "chromium"]);
            if (exitCode != 0)
                await logFunction(LogLevel.Warning, $"Playwright browser install returned exit code {exitCode}");
        }
        catch (Exception ex)
        {
            await logFunction(LogLevel.Warning, $"Failed to install Playwright browsers: {ex.Message}");
        }

        return null;
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config,
        IEnumerable<ToolCall> enumerableToolCalls)
    {
        try
        {
            LastConfig = config;
            var browser = GetBrowser(config, Logger);
            var service = new LinkedInService(browser, config, NotificationService, RateLimiter, Logger);
            return await service.ProcessRequest(RawServiceRequest, config, NotificationService);
        }
        catch (Exception ex)
        {
            await Log(LogLevel.Error, $"Error executing action '{serviceRequest.Method}'", ex);
            return new { Success = false, Message = $"Error: {ex.Message}" };
        }
    }

    /// <summary>
    /// Returns a cached browser for the configured account, recreating it if the account
    /// changed. Caching matters: a persistent Chromium profile cannot be opened twice
    /// concurrently, and re-launching per call would be slow and detection-prone.
    /// </summary>
    private static LinkedInBrowser GetBrowser(ServiceConfig config, LogDelegate log)
    {
        var account = LinkedInPaths.SanitizeAccount(config.AccountLabel);

        if (LinkedInLoginSession.ActiveFor(config.AccountLabel) is not null)
            throw new InvalidOperationException("LinkedIn login is in progress. Complete the interactive login first, then retry.");

        lock (BrowserLock)
        {
            if (_browser is not null && _browserAccount == account) return _browser;
            _browser?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _browser = new LinkedInBrowser(config, log);
            _browserAccount = account;
            return _browser;
        }
    }

    /// <summary>
    /// Closes and nulls the cached production browser for an account. Called by the login
    /// session before opening the same profile dir (to avoid the "already in use" error)
    /// and again after login completes (so the next tool call gets a fresh authenticated context).
    /// </summary>
    internal static void InvalidateBrowser(string accountLabel)
    {
        var account = LinkedInPaths.SanitizeAccount(accountLabel);
        lock (BrowserLock)
        {
            if (_browser is null || _browserAccount != account) return;
            _browser.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _browser = null;
            _browserAccount = null;
        }
    }

    // IHasFrontend -----------------------------------------------------------

    public FrontendManifest GetFrontendManifest() => new(
        EntryPath: "ui/index.html",
        Pages: new[]
        {
            new FrontendPage("/", "LinkedIn Login", IconName: "linkedin", SidebarGroup: "Plugins")
        });

    // IHasHttpRoutes ---------------------------------------------------------

    public void MapRoutes(IEndpointRouteBuilder routes)
    {
        Endpoints.LinkedInLoginEndpoints.Map(routes);
    }
}
