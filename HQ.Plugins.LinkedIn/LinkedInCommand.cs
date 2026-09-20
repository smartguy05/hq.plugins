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
    // never share a slot.
    //
    // WP6A-7 (third pass, CLOSED): earlier passes left the production per-agent path (DoWork
    // below) resolving Guid.Empty for every caller, because CommandBase<T,TU>.Execute/DoWork,
    // ICommand and OrchestratorRequest carry no caller org id of their own. That gap is closed
    // here the same way HQ.Plugins.FileStorage's WP6A-5 closed it: HQ.Services.Plugin.PluginService
    // .InjectOrganizationId force-overwrites `organizationId` onto every plugin tool call's
    // ServiceRequest JSON, server-side, from the calling agent's authoritative
    // agent.OrganizationId, before CommandBase.Execute ever deserializes it — see
    // Models.ServiceRequest.OrganizationId's doc. DoWork now requires that value (fails closed,
    // see RequireOrganizationId) and threads the REAL org id into GetBrowser, so two different
    // orgs sharing a default AccountLabel resolve to distinct cached browsers AND distinct
    // on-disk profiles (LinkedInPaths.ProfileDir(orgId, accountLabel)) — the same directory
    // LinkedInLoginSession now authenticates that org into (see its OrgId usage). No static or
    // Guid.Empty-keyed shared browser is reachable from DoWork any more.
    private static readonly RateLimitGate RateLimiter = new();
    private static readonly object BrowserLock = new();
    private static readonly Dictionary<string, LinkedInBrowser> _browsers = new();

    /// <summary>
    /// Last config seen by the plugin — used by the login endpoints, which run outside agent
    /// context. Known, disclosed residual (re-review minor #2, out of this WP6A-7 pass's scope):
    /// this is a single process-wide static written by every org's <see cref="DoWork"/> call, so
    /// a login request can, purely by timing, read a DIFFERENT org's ancillary settings
    /// (AccountLabel, locale, timezone, UA, rate limits) here. It no longer enables a cross-org
    /// session takeover by itself — <see cref="GetBrowser"/> now refuses a mismatched org outright
    /// and resolves a per-org profile — but a per-org config cache (keyed the same way
    /// <see cref="_browsers"/> now is) would be needed to close this ancillary-settings leak too;
    /// tracked as a separate follow-up, not this finding's core impact.
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

    /// <summary>
    /// WP6A-7 (third pass): every production tool call must carry a caller-scoped organization
    /// id. HQ.Services.Plugin.PluginService.InjectOrganizationId force-overwrites
    /// <see cref="ServiceRequest.OrganizationId"/> with the calling agent's authoritative
    /// OrganizationId before this plugin ever sees the call — the model cannot forge a different
    /// tenant's id here. A null/empty value means the call did not come through that host path
    /// (or org is genuinely absent), so we FAIL CLOSED rather than falling back to the
    /// unscoped/shared browser and profile. Mirrors HQ.Plugins.FileStorage.FileStorageService
    /// .RequireOrganizationId (WP6A-5), the sibling fix for the same host-injected field.
    /// </summary>
    internal static Guid RequireOrganizationId(Guid? organizationId)
    {
        if (organizationId is null || organizationId == Guid.Empty)
            throw new UnauthorizedAccessException(
                "Missing organization context; refusing to drive the LinkedIn browser/profile without a caller-scoped organization id.");
        return organizationId.Value;
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config,
        IEnumerable<ToolCall> enumerableToolCalls)
    {
        try
        {
            LastConfig = config;
            var orgId = RequireOrganizationId(serviceRequest.OrganizationId);
            var browser = GetBrowser(config, Logger, orgId);
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
    ///
    /// WP6A-7 (third pass): <paramref name="orgId"/> is mandatory and must already be the
    /// caller's real, resolved org (see <see cref="RequireOrganizationId"/> — DoWork never calls
    /// this with a default/unscoped id). Before handing back a browser, this also refuses when
    /// the resolved profile directory's ownership marker (see <see cref="LinkedInPaths.ReadProfileOwner"/>)
    /// already names a DIFFERENT org — defense in depth alongside the per-org directory itself,
    /// covering a profile directory reused/restored/migrated outside the normal login flow.
    /// Internal (not private) so it is directly unit-testable without a live Playwright session.
    /// </summary>
    internal static LinkedInBrowser GetBrowser(ServiceConfig config, LogDelegate log, Guid orgId)
    {
        var key = CacheKey(orgId, config.AccountLabel);

        if (LinkedInLoginSession.ActiveFor(config.AccountLabel, orgId) is not null)
            throw new InvalidOperationException("LinkedIn login is in progress. Complete the interactive login first, then retry.");

        var profileDir = LinkedInPaths.ProfileDir(orgId, config.AccountLabel);
        var owner = LinkedInPaths.ReadProfileOwner(profileDir);
        if (LinkedInPaths.IsProfileOwnedByAnotherOrg(orgId, owner))
            throw new InvalidOperationException(
                "This LinkedIn account label's profile is connected by a different organization. " +
                "Choose a distinct account label for this organization.");

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

    /// <summary>
    /// Test-only reset hook for the process-wide statics this command caches
    /// (<see cref="LastConfig"/> and the per-(org, account) browser cache). Production code never
    /// calls this. It exists so xUnit test classes that drive <see cref="DoWork"/> or
    /// <see cref="GetBrowser"/> directly (both of which mutate this process-wide state as a
    /// documented, disclosed side effect — see <see cref="LastConfig"/>'s doc comment) can restore
    /// a clean slate between tests instead of leaking one test's synthetic
    /// <c>ServiceConfig.AccountLabel</c> (e.g. "wp6a7-empty-org-&lt;hex&gt;" from
    /// LinkedInCommandTests.DoWork_EmptyGuidOrganizationId_IsRefused) into
    /// LinkedInLoginEndpoints.ResolveConfig, which every LinkedInTenancyTests.ResolveConfig_* test
    /// reads via <see cref="LastConfig"/>. See LinkedInCommandTests and LinkedInTenancyTests'
    /// shared <c>[Collection("LinkedIn command static state")]</c> for the other half of the fix
    /// (those two classes must not run concurrently on separate threads either, or resetting here
    /// alone cannot prevent the race).
    /// </summary>
    internal static void ResetForTests()
    {
        LastConfig = null;
        lock (BrowserLock)
        {
            foreach (var browser in _browsers.Values)
            {
                try { browser.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                catch { /* best-effort cleanup; a test's own assertions already ran */ }
            }
            _browsers.Clear();
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
