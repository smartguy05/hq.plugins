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
    private LinkedInBrowser _browser;
    private CancellationTokenSource _pollCts;

    public string Account { get; }
    public int Display { get; private set; }
    public int VncWebPort { get; private set; }
    public bool Started { get; private set; }
    public bool Authenticated { get; private set; }
    public string Error { get; private set; }

    private LinkedInLoginSession(ServiceConfig config, LogDelegate log)
    {
        _config = config;
        _log = log;
        Account = LinkedInPaths.SanitizeAccount(config.AccountLabel);
    }

    /// <summary>Returns the in-flight login session for an account, if any.</summary>
    public static LinkedInLoginSession ActiveFor(string accountLabel)
    {
        var key = LinkedInPaths.SanitizeAccount(accountLabel);
        lock (Lock) return Active.TryGetValue(key, out var s) ? s : null;
    }

    /// <summary>Starts (or returns the existing) login session for the configured account.</summary>
    public static async Task<LinkedInLoginSession> StartAsync(ServiceConfig config, LogDelegate log)
    {
        var key = LinkedInPaths.SanitizeAccount(config.AccountLabel);
        LinkedInLoginSession session;
        lock (Lock)
        {
            if (Active.TryGetValue(key, out var existing)) return existing;
            session = new LinkedInLoginSession(config, log);
            Active[key] = session;
        }

        await session.LaunchAsync();
        return session;
    }

    private async Task LaunchAsync()
    {
        try
        {
            // Deterministic, collision-resistant display/port per account.
            var slot = (uint)Account.GetHashCode() % 200;
            Display = (int)(100 + slot);
            VncWebPort = (int)(6100 + slot);
            var displayStr = $":{Display}";

            StartProcess("Xvfb", $"{displayStr} -screen 0 1280x800x24 -nolisten tcp");
            await Task.Delay(800);

            _browser = new LinkedInBrowser(_config, _log, displayOverride: displayStr);
            // Headed navigation to the login page primes the window the user will see.
            await _browser.VoyagerAsync("GET", "/voyager/api/me"); // forces context launch + nav to origin

            StartProcess("x11vnc", $"-display {displayStr} -nopw -forever -shared -quiet -rfbport {5900 + Display}");
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
                        await DisposeAsync(); // tear down VNC + flush profile to disk
                        return;
                    }
                }
                catch { /* keep polling */ }
                await Task.Delay(3000, token).ContinueWith(_ => { });
            }
        }, token);
    }

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

        lock (Lock)
        {
            if (Active.TryGetValue(Account, out var s) && ReferenceEquals(s, this))
                Active.Remove(Account);
        }
    }
}
