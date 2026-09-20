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

    // Shared across requests in this process: one rate-limit gate, one cached browser per
    // (org, account) key.
    //
    // WP6A-7: this used to be a single static slot (`_browser`/`_browserAccount`) shared by
    // every tenant in the process — whichever agent called DoWork most recently owned the one
    // live browser, so a second tenant's call disposed and replaced it (thrashing), and two
    // tenants racing on the same account label could hand each other the exact same live,
    // authenticated LinkedIn session. Keying by (org, account) instead means distinct tenants
    // never share a slot -- for the interactive /login/* endpoints, which resolve a real,
    // header-validated org id (see Endpoints.LinkedInLoginEndpoints.ResolveConfig).
    //
    // WP6A-7 STILL OPEN, NOT CLOSED, DO NOT REMOVE THIS NOTE WITHOUT CLOSING IT FOR REAL: the
    // production per-agent path (DoWork below) has no organization id available to it at all
    // (confirmed by inspecting HQ.Models.Interfaces.CommandBase<T,TU>.Execute/DoWork,
    // HQ.Models.Interfaces.ICommand, and HQ.Models.OrchestratorRequest -- none carry a caller org
    // id, only an optional AgentId that DoWork never receives), so GetBrowser always keys on
    // Guid.Empty + account label. Two different orgs that both leave AccountLabel at its shared
    // "default" value will transparently share the same cached LinkedInBrowser and on-disk
    // profile via nothing more than ordinary tool calls -- no login-endpoint race required. This
    // is the finding's own confirmed core impact and it is NOT mitigated by the atomic
    // pre-launch claim in LinkedInLoginSession.StartAsync, which only guards the interactive
    // login window. Closing it for real needs an org id threaded through HQ.Models'
    // CommandBase/OrchestratorRequest, a host-wide, cross-repo change out of HQ.Plugins.LinkedIn's
    // scope; ServiceConfig.AccountLabel's tooltip now warns operators to use a distinct label per
    // organization as a compensating control until that lands. This is a genuine finding
    // (WP6A-7) residual and must be carried as a deferred item, not reported as closed.
    private static readonly RateLimitGate RateLimiter = new();
    private static readonly object BrowserLock = new();
    private static readonly Dictionary<string, LinkedInBrowser> _browsers = new();

    /// <summary>
    /// Last config seen by the plugin — used by the login endpoints, which run outside agent
    /// context. Known, disclosed residual (re-review minor #2): this is a single process-wide
    /// static written by every org's <see cref="DoWork"/> call, so a login request can, purely by
    /// timing, read a DIFFERENT org's ancillary settings (AccountLabel, locale, timezone, UA,
    /// rate limits) here. It no longer enables a cross-org session takeover by itself — see
    /// <see cref="LinkedInLoginSession.EnsureProfileNotOwnedByAnotherOrg"/> — but is not fully
    /// org-scoped, because <see cref="DoWork"/> has no caller org id to key a per-org cache by
    /// (the same host-wide HQ.Models gap as WP6A-7's production-profile residual below).
    /// </summary>
    internal static ServiceConfig LastConfig { get; private set; }

    /// <summary>Cache/session key for the (org, account) pair — never derived from caller input alone.</summary>
    internal static string CacheKey(Guid orgId, string accountLabel) =>
        $"{LinkedInPaths.SanitizeOrg(orgId) ?? "noorg"}:{LinkedInPaths.SanitizeAccount(accountLabel)}";

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
    /// Returns a cached browser for the (org, account) key, creating one if needed. Caching
    /// matters: a persistent Chromium profile cannot be opened twice concurrently, and
    /// re-launching per call would be slow and detection-prone. Unlike the pre-WP6A-7 single
    /// static slot, a distinct key gets its own cached instance instead of evicting whatever
    /// tenant was cached before it.
    /// </summary>
    private static LinkedInBrowser GetBrowser(ServiceConfig config, LogDelegate log, Guid orgId = default)
    {
        var key = CacheKey(orgId, config.AccountLabel);

        if (LinkedInLoginSession.ActiveFor(config.AccountLabel, orgId) is not null)
            throw new InvalidOperationException("LinkedIn login is in progress. Complete the interactive login first, then retry.");

        lock (BrowserLock)
        {
            if (_browsers.TryGetValue(key, out var existing)) return existing;
            var browser = new LinkedInBrowser(config, log, orgId: orgId);
            _browsers[key] = browser;
            return browser;
        }
    }

    /// <summary>
    /// Closes and drops the cached production browser for an (org, account) key. Called by the
    /// login session before opening the same profile dir (to avoid the "already in use" error)
    /// and again after login completes (so the next tool call gets a fresh authenticated context).
    /// </summary>
    internal static void InvalidateBrowser(string accountLabel, Guid orgId = default)
    {
        var key = CacheKey(orgId, accountLabel);
        lock (BrowserLock)
        {
            if (!_browsers.TryGetValue(key, out var browser)) return;
            browser.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _browsers.Remove(key);
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
