using HQ.Plugins.LinkedIn.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HQ.Plugins.LinkedIn.Endpoints;

/// <summary>
/// HTTP routes backing the one-time interactive login. The flow: POST <c>/login/start</c>
/// spins up a headed Chromium inside a virtual display exposed over noVNC; the UI polls
/// <c>/login/status</c> and embeds the noVNC viewer so the user can type their LinkedIn
/// credentials directly into the real browser. Credentials never pass through these routes —
/// only the live/authenticated status does.
/// </summary>
public static class LinkedInLoginEndpoints
{
    public static void Map(IEndpointRouteBuilder routes)
    {
        routes.MapPost("/login/start", async (HttpContext ctx) =>
        {
            var (config, orgId) = ResolveConfig(ctx);
            try
            {
                var session = await LinkedInLoginSession.StartAsync(config, null, orgId);
                return Results.Json(Describe(session));
            }
            catch (InvalidOperationException ex)
            {
                // WP6A-7 re-review blocking #1: thrown by
                // LinkedInLoginSession.EnsureProfileNotOwnedByAnotherOrg when this org's target
                // profile directory was already authenticated by a different org — never start a
                // browser on it, and never leak whose account it is beyond this generic message.
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status409Conflict);
            }
        });

        routes.MapGet("/login/status", (HttpContext ctx) =>
        {
            var (config, orgId) = ResolveConfig(ctx);
            var session = LinkedInLoginSession.ActiveFor(config.AccountLabel, orgId);
            return session is null
                ? Results.Json(new { active = false })
                : Results.Json(Describe(session));
        });

        routes.MapPost("/login/cancel", async (HttpContext ctx) =>
        {
            var (config, orgId) = ResolveConfig(ctx);
            var session = LinkedInLoginSession.ActiveFor(config.AccountLabel, orgId);
            if (session is not null) await session.DisposeAsync();
            return Results.Json(new { cancelled = true });
        });
    }

    private static object Describe(LinkedInLoginSession s) => new
    {
        active = true,
        s.Account,
        s.Started,
        s.Authenticated,
        s.Display,
        s.VncWebPort,
        s.Error,
        vncPath = $"/vnc.html?autoconnect=true&resize=remote&port={s.VncWebPort}"
    };

    /// <summary>
    /// Login runs outside agent context, so there's no encrypted per-agent config to hand — we
    /// use the last config the plugin saw, AccountLabel included, for ancillary
    /// browser-fingerprint settings (locale, timezone, user agent) <b>and</b> for which on-disk
    /// profile the interactive login actually authenticates. No secrets are needed here — the
    /// session is captured interactively.
    ///
    /// WP6A-7: this route group is mounted once per process and reached by any authenticated
    /// caller, so the account it targets can never come from caller-supplied input. It used to
    /// accept an unauthenticated <c>?account=</c> query override — removed below, the query
    /// string is never read for the account any more. The caller's own organization id (returned
    /// alongside the config) is what <c>TenantResolutionMiddleware</c> (host-side) has already
    /// validated and rewritten onto <c>X-Organization-Id</c> before this handler runs, so it
    /// cannot be spoofed by the caller; it scopes the *login session's own bookkeeping* — see
    /// <see cref="LinkedInCommand.CacheKey"/> and <see cref="LinkedInLoginSession.ActiveFor"/> —
    /// so one org can never observe, cancel or launch a browser tied to another org's in-flight
    /// login, even when both share the same ambient AccountLabel.
    ///
    /// AccountLabel itself is deliberately <b>not</b> derived from the org id (that was a
    /// regression caught in re-review): <see cref="LinkedInPaths.ProfileDir"/> combines
    /// (orgId, accountLabel), and overwriting AccountLabel with an org-derived string made the
    /// login flow authenticate into a directory that <see cref="LinkedInCommand.DoWork"/>'s
    /// production tool-call path — which has no caller org id available to it yet and always
    /// resolves under <see cref="Guid.Empty"/>, see the WP6A-7 notes on that call site — could
    /// never find. Keeping AccountLabel untouched means <see cref="LinkedInLoginSession.ProfileOrgId"/>
    /// (also <see cref="Guid.Empty"/>) plus this AccountLabel reproduces exactly the profile
    /// directory production will read from. Since that means every org's login targets the SAME
    /// directory for a shared AccountLabel, <see cref="LinkedInLoginSession.StartAsync"/> guards
    /// it with <see cref="LinkedInLoginSession.EnsureProfileNotOwnedByAnotherOrg"/> — a different,
    /// already-authenticated org is refused rather than handed that org's live session. Fully
    /// scoping the directory itself by org too (so two orgs never need to negotiate the same
    /// bucket at all) needs an org id threaded through HQ.Models' CommandBase/OrchestratorRequest
    /// — a host-wide change out of this plugin's scope (tracked as a follow-up, not fixed here).
    ///
    /// <b>Known, disclosed residual (re-review minor #2):</b> the ancillary settings this returns
    /// (AccountLabel, locale, timezone, UA, RequiresConfirmation, rate limits) come from
    /// <see cref="LinkedInCommand.LastConfig"/>, a single process-wide static last written by
    /// WHICHEVER org's <see cref="LinkedInCommand.DoWork"/> call ran most recently — not
    /// necessarily this caller's own org-scoped config. This can't be fixed here either: DoWork
    /// has no caller org id to key a per-org config cache by (the same host-wide gap as above).
    /// It no longer enables a cross-org profile/session takeover on its own — that's what
    /// <see cref="LinkedInLoginSession.EnsureProfileNotOwnedByAnotherOrg"/> now closes — but the
    /// browser fingerprint (locale/timezone/UA) an org's login uses can still, by timing alone,
    /// be a different org's settings. Left as a documented follow-up rather than a speculative
    /// fix that could regress single-tenant/dev deployments (a naive per-org config cache would
    /// go stale forever for the common Guid.Empty/no-tenancy case, since DoWork never gets a real
    /// org id to invalidate it by).
    /// </summary>
    internal static (ServiceConfig Config, Guid OrgId) ResolveConfig(HttpContext ctx)
    {
        var orgId = ResolveCallerOrgId(ctx);
        var config = LinkedInCommand.LastConfig ?? new ServiceConfig();
        return (config, orgId);
    }

    /// <summary>
    /// Parses the validated caller org from the request header. Pure w.r.t. the header value so
    /// it's unit-testable without a full HTTP host. An absent/unparseable header resolves to
    /// <see cref="Guid.Empty"/> — the same "unscoped" bucket <see cref="LinkedInPaths.ProfileDir"/>
    /// already used before WP6A-7, so a deployment with tenancy disabled keeps its prior behavior.
    /// </summary>
    internal static Guid ResolveCallerOrgId(HttpContext ctx) =>
        ResolveCallerOrgId(ctx.Request.Headers["X-Organization-Id"].ToString());

    internal static Guid ResolveCallerOrgId(string orgHeaderValue) =>
        Guid.TryParse(orgHeaderValue, out var orgId) ? orgId : Guid.Empty;
}
