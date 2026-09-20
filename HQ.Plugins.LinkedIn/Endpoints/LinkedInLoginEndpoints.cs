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
    /// (orgId, accountLabel) on its own, so overwriting AccountLabel with an org-derived string
    /// would make (orgId, org-derived-label) instead of (orgId, real-label) — an extra,
    /// unnecessary level of org-scoping that would also make an operator's configured
    /// AccountLabel meaningless. The org id returned alongside the config is what actually scopes
    /// the profile directory (see <see cref="LinkedInCommand.GetBrowser"/> and
    /// <see cref="LinkedInLoginSession.StartAsync"/>, both of which resolve
    /// <c>LinkedInPaths.ProfileDir(orgId, accountLabel)</c> for this same real, resolved org — closing
    /// the WP6A-7 residual earlier passes left open, where the production tool-call path had no
    /// caller org id and always resolved under <see cref="Guid.Empty"/>). Two different, resolved
    /// orgs therefore no longer even contend for the same directory; <see cref="LinkedInLoginSession.EnsureProfileNotOwnedByAnotherOrg"/>
    /// and <see cref="LinkedInPaths.TryClaimProfile"/> remain as defense in depth (a manually
    /// copied/restored profile, or the shared bucket a tenancy-disabled deployment still uses).
    ///
    /// <b>Known, disclosed residual (re-review minor #2, out of WP6A-7's scope):</b> the ancillary
    /// settings this returns (AccountLabel, locale, timezone, UA, RequiresConfirmation, rate
    /// limits) come from <see cref="LinkedInCommand.LastConfig"/>, a single process-wide static
    /// last written by WHICHEVER org's <see cref="LinkedInCommand.DoWork"/> call ran most
    /// recently — not necessarily this caller's own org-scoped config. It no longer enables a
    /// cross-org profile/session takeover on its own — that's what the per-org profile directory
    /// plus <see cref="LinkedInCommand.GetBrowser"/>'s ownership check now close — but the browser
    /// fingerprint (locale/timezone/UA) an org's login uses can still, by timing alone, be a
    /// different org's settings. A per-org config cache (now straightforward, since DoWork has a
    /// real org id to key and invalidate one by) is left as a documented follow-up rather than
    /// folded into this pass.
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
