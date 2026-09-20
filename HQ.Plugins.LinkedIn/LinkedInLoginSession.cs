using System.Diagnostics;
using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.LinkedIn.Models;

namespace HQ.Plugins.LinkedIn;

/// <summary>
/// Orchestrates the one-time interactive login. On a headless server there is no desktop, so
/// we run a <b>headed</b> Chromium inside a virtual display (Xvfb), expose that display over
/// VNC (x11vnc) and bridge it to the browser via websockify/noVNC. The user opens the served
/// page, sees the real LinkedIn login form, and types their credentials (and solves any 2FA /
/// checkpoint / CAPTCHA) <b>directly into Chromium as VNC pixels</b> — the password never
/// enters HQ logic or the model. On success the session is already persisted in the on-disk
/// profile, so steady-state use needs no further login.
///
/// The external tools (xvfb, x11vnc, websockify + noVNC web root) must be present on the host;
/// missing tools surface as a clear error rather than a crash. This path is integration-only
/// and cannot run in CI.
/// </summary>
public sealed class LinkedInLoginSession : IAsyncDisposable
{
    private static readonly Dictionary<string, LinkedInLoginSession> Active = new();
    private static readonly object Lock = new();

    private readonly ServiceConfig _config;
    private readonly LogDelegate _log;
    private readonly List<Process> _procs = new();
    private readonly string _key;
    private LinkedInBrowser _browser;
    private CancellationTokenSource _pollCts;
    private bool _claimedProfileFresh;

    public string Account { get; }
    public Guid OrgId { get; }
    public int Display { get; private set; }
    public int VncWebPort { get; private set; }
    public bool Started { get; private set; }
    public bool Authenticated { get; private set; }
    public string Error { get; private set; }

    private LinkedInLoginSession(ServiceConfig config, LogDelegate log, Guid orgId)
    {
        _config = config;
        _log = log;
        OrgId = orgId;
        Account = LinkedInPaths.SanitizeAccount(config.AccountLabel);
        _key = LinkedInCommand.CacheKey(orgId, config.AccountLabel);
    }

    /// <summary>
    /// Returns the in-flight login session for an (org, account) key, if any. WP6A-7: keying by
    /// org (not account text alone) means a caller from one org can never observe or cancel
    /// another org's in-flight login, even if both leave the account label at its shared default.
    /// </summary>
    public static LinkedInLoginSession ActiveFor(string accountLabel, Guid orgId = default)
    {
        var key = LinkedInCommand.CacheKey(orgId, accountLabel);
        lock (Lock) return Active.TryGetValue(key, out var s) ? s : null;
    }

    /// <summary>Starts (or returns the existing) login session for the (org, account) key.</summary>
    public static async Task<LinkedInLoginSession> StartAsync(ServiceConfig config, LogDelegate log, Guid orgId = default)
    {
        var key = LinkedInCommand.CacheKey(orgId, config.AccountLabel);
        lock (Lock)
        {
            if (Active.TryGetValue(key, out var existing)) return existing;
        }

        // WP6A-7 (third pass): the browser this session opens now targets
        // LinkedInPaths.ProfileDir(orgId, accountLabel) — the SAME per-org directory
        // LinkedInCommand.GetBrowser resolves for this org's production tool calls (see its
        // doc) — instead of the pre-third-pass shared Guid.Empty bucket every org's login used
        // to authenticate into. Two different, resolved orgs can no longer even contend for the
        // same directory (they resolve to physically distinct paths), but TryClaimProfile is kept
        // as the atomic pre-launch guard against a SAME-org double start (e.g. two concurrent
        // /login/start clicks) racing onto the same user-data-dir before either one authenticates
        // (the TOCTOU re-review originally flagged as still-open) and, for a tenancy-disabled
        // deployment where every caller resolves to the shared Guid.Empty bucket, still guards
        // that shared bucket exactly as before.
        var profileDir = LinkedInPaths.ProfileDir(orgId, config.AccountLabel);
        var claim = LinkedInPaths.TryClaimProfile(orgId, profileDir);
        if (claim == LinkedInPaths.ProfileClaim.Rejected)
        {
            throw new InvalidOperationException(
                "This LinkedIn account label is already connected by a different organization. " +
                "Choose a distinct account label for this organization before starting login.");
        }

        LinkedInLoginSession session;
        lock (Lock)
        {
            if (Active.TryGetValue(key, out var existing)) return existing;
            session = new LinkedInLoginSession(config, log, orgId)
            {
                // Only a FRESH claim is ours to release on failure/cancel — AlreadyOwned means the
                // marker predates this call (e.g. our own org's prior successful login) and must
                // survive this session tearing down, or a failed retry would erase a real, valid
                // ownership record.
                _claimedProfileFresh = claim == LinkedInPaths.ProfileClaim.ClaimedFresh
            };
            Active[key] = session;
        }

        await session.LaunchAsync();
        return session;
    }

    /// <summary>
    /// Refuses to start a login for <paramref name="callerOrgId"/> against a profile directory
    /// already authenticated by a different, resolved org. Extracted as a pure(-ish) function
    /// (I/O only via the marker file at <paramref name="profileDir"/>) so it's unit-testable with
    /// a throwaway temp directory, without spawning Xvfb/x11vnc/Chromium.
    ///
    /// Second pass: <see cref="StartAsync"/> no longer calls this directly — it only ever
    /// consults a marker written <b>after</b> <see cref="Authenticated"/> becomes true, which left
    /// the pre-authentication window open to a concurrent racing start (the TOCTOU re-review
    /// flagged as still-open). <see cref="LinkedInPaths.TryClaimProfile"/> is the atomic
    /// replacement StartAsync now uses; this method is kept as the underlying read-only
    /// "who owns this directory right now" check (still exercised by its own direct unit tests)
    /// and remains correct to call wherever only a non-mutating check, not a claim, is needed.
    /// </summary>
    internal static void EnsureProfileNotOwnedByAnotherOrg(Guid callerOrgId, string profileDir)
    {
        var owner = LinkedInPaths.ReadProfileOwner(profileDir);
        if (!LinkedInPaths.IsProfileOwnedByAnotherOrg(callerOrgId, owner)) return;

        throw new InvalidOperationException(
            "This LinkedIn account label is already connected by a different organization. " +
            "Choose a distinct account label for this organization before starting login.");
    }

    private async Task LaunchAsync()
    {
        try
        {
            // Close the production browser first — the persistent profile dir can't be opened
            // twice. WP6A-7 (third pass): OrgId is now the exact same key
            // LinkedInCommand.DoWork's GetBrowser caches under for this org.
            LinkedInCommand.InvalidateBrowser(Account, OrgId);

            // Deterministic, collision-resistant display/port per (org, account) key — WP6A-7:
            // hashing the account label alone let two tenants sharing a default label collide
            // on the same X11 display and VNC port for concurrent interactive logins.
            var slot = (uint)_key.GetHashCode() % 200;
            Display = (int)(100 + slot);
            VncWebPort = (int)(6100 + slot);
            var displayStr = $":{Display}";

            StartProcess("Xvfb", $"{displayStr} -screen 0 1280x800x24 -nolisten tcp");
            await Task.Delay(800);

            // Always headed — the user must see the browser window via noVNC. orgId: OrgId (this
            // session's real, resolved caller org) so the profile this authenticates into is the
            // exact one LinkedInCommand.DoWork's production path will read from for this org.
            _browser = new LinkedInBrowser(_config, _log, displayOverride: displayStr, forceHeaded: true, orgId: OrgId);
            // Headed navigation to the login page primes the window the user will see.
            await _browser.VoyagerAsync("GET", "/voyager/api/me"); // forces context launch + nav to origin

            StartProcess("x11vnc", X11VncArgs(displayStr, Display));
            await Task.Delay(400);
            StartProcess("websockify", $"--web=/usr/share/novnc/ {VncWebPort} localhost:{5900 + Display}");

            Started = true;
            BeginPolling();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            _log?.Invoke(LogLevel.Error, $"LinkedIn login session failed to start: {ex.Message}");
            await DisposeAsync();
        }
    }

    private void BeginPolling()
    {
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;
        _ = Task.Run(async () =>
        {
            // Poll for up to ~10 minutes for the user to finish logging in.
            for (var i = 0; i < 200 && !token.IsCancellationRequested; i++)
            {
                try
                {
                    if (_browser != null && await _browser.IsAuthenticatedAsync())
                    {
                        Authenticated = true;
                        _log?.Invoke(LogLevel.Info, $"LinkedIn session authenticated for '{Account}'.");
                        // WP6A-7: stamp this org's own profile dir with its owner BEFORE tearing
                        // down. Now that every org resolves a physically distinct directory, this
                        // is normally a self-claim, but it stays load-bearing for a
                        // tenancy-disabled deployment (every caller resolves Guid.Empty, i.e. the
                        // shared bucket) and as defense in depth against a manually
                        // copied/restored profile directory.
                        LinkedInPaths.WriteProfileOwner(
                            LinkedInPaths.ProfileDir(OrgId, _config.AccountLabel), OrgId);
                        await DisposeAsync(); // tear down VNC + flush profile to disk
                        // Invalidate any stale production browser so the next tool call opens a
                        // fresh context on the now-authenticated profile — same (org, account)
                        // key LinkedInCommand.DoWork's GetBrowser caches under for this org.
                        LinkedInCommand.InvalidateBrowser(Account, OrgId);
                        return;
                    }
                }
                catch { /* keep polling */ }
                await Task.Delay(3000, token).ContinueWith(_ => { });
            }
        }, token);
    }

    /// <summary>
    /// x11vnc arguments for the login display. WP6A-11: <c>-localhost</c> binds the VNC server to
    /// loopback only, so the raw, unauthenticated (<c>-nopw</c>) VNC port is reachable solely via a
    /// local proxy/tunnel (websockify, bound above to <c>localhost</c>) rather than from any host
    /// network interface. Extracted as a pure function so the flag is unit-testable without
    /// actually spawning x11vnc.
    /// </summary>
    internal static string X11VncArgs(string displayStr, int display) =>
        $"-display {displayStr} -nopw -localhost -forever -shared -quiet -rfbport {5900 + display}";

    private void StartProcess(string fileName, string args)
    {
        var psi = new ProcessStartInfo(fileName, args)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}'. Is it installed on the host?");
        _procs.Add(proc);
    }

    public async ValueTask DisposeAsync()
    {
        _pollCts?.Cancel();

        if (_browser != null)
        {
            await _browser.DisposeAsync();
            _browser = null;
        }

        foreach (var p in _procs)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); }
            catch { /* ignore */ }
            p.Dispose();
        }
        _procs.Clear();

        // WP6A-7 re-review blocking #1 (second pass): release a claim THIS session made fresh if
        // the login never reached Authenticated (cancelled, failed to launch, or timed out) — a
        // claim made in StartAsync but never completed must not permanently squat on the account
        // label, including for this same org's own retry. A completed (Authenticated) session
        // must NOT release it: that marker is now the real, load-bearing ownership record
        // EnsureProfileNotOwnedByAnotherOrg / TryClaimProfile check on every future call.
        if (_claimedProfileFresh && !Authenticated)
        {
            LinkedInPaths.ReleaseProfileClaim(LinkedInPaths.ProfileDir(OrgId, _config.AccountLabel));
        }

        lock (Lock)
        {
            if (Active.TryGetValue(_key, out var s) && ReferenceEquals(s, this))
                Active.Remove(_key);
        }
    }
}
