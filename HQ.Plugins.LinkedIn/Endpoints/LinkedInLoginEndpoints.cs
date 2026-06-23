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
            var config = ResolveConfig(ctx);
            var session = await LinkedInLoginSession.StartAsync(config, null);
            return Results.Json(Describe(session));
        });

        routes.MapGet("/login/status", (HttpContext ctx) =>
        {
            var config = ResolveConfig(ctx);
            var session = LinkedInLoginSession.ActiveFor(config.AccountLabel);
            return session is null
                ? Results.Json(new { active = false })
                : Results.Json(Describe(session));
        });

        routes.MapPost("/login/cancel", async (HttpContext ctx) =>
        {
            var config = ResolveConfig(ctx);
            var session = LinkedInLoginSession.ActiveFor(config.AccountLabel);
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
    /// Login runs outside agent context, so there's no encrypted per-agent config to hand.
    /// Use the last config the plugin saw, allowing an <c>?account=</c> override, and fall
    /// back to defaults. No secrets are needed here — the session is captured interactively.
    /// </summary>
    private static ServiceConfig ResolveConfig(HttpContext ctx)
    {
        var config = LinkedInCommand.LastConfig is null
            ? new ServiceConfig { AccountLabel = "default" }
            : LinkedInCommand.LastConfig with { };

        var account = ctx.Request.Query["account"].ToString();
        if (!string.IsNullOrWhiteSpace(account)) config.AccountLabel = account;
        return config;
    }
}
