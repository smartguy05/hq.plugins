using HQ.Models.Helpers;

namespace HQ.Plugins.LinkedIn;

/// <summary>
/// Resolves the on-disk location of the persistent LinkedIn browser profile.
/// The profile directory holds Chromium's user-data-dir for a connected account —
/// including the httpOnly <c>li_at</c> auth cookie that JavaScript cannot reach —
/// so the agent can reuse the session across restarts without ever handling the
/// password. Mirrors <c>HQ.Plugins.Email.EmailPaths</c>: prefers the shared writable
/// plugin data space (<c>HQ_PLUGIN_DATA_DIR</c>) and falls back to a folder next to
/// the plugin DLL for local dev.
/// </summary>
public static class LinkedInPaths
{
    /// <summary>Root directory holding per-account LinkedIn browser profiles.</summary>
    public static string DataDir()
    {
        var pluginDir = Path.GetDirectoryName(typeof(LinkedInPaths).Assembly.Location)!;
        return PluginDataDirectory.Resolve("HQ.Plugins.LinkedIn", Path.Combine(pluginDir, "LinkedInData"));
    }

    /// <summary>
    /// Sanitizes an account label into a filesystem-safe folder segment so an
    /// arbitrary config value can never escape the data directory.
    /// </summary>
    public static string SanitizeAccount(string accountLabel)
    {
        if (string.IsNullOrWhiteSpace(accountLabel)) return "default";
        var chars = accountLabel.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray();
        var cleaned = new string(chars).Trim('-');
        return string.IsNullOrEmpty(cleaned) ? "default" : cleaned;
    }

    /// <summary>
    /// Sanitizes a caller-resolved organization id into a filesystem-safe folder segment.
    /// <see cref="Guid"/>'s "N" format is hex-only, so this can never traverse. An unresolved
    /// org (<see cref="Guid.Empty"/>) maps to no segment at all, which keeps
    /// <see cref="ProfileDir"/> byte-for-byte identical to its pre-WP6A-7 (account-only) form
    /// for the production agent path, which has no org id to give it (see WP6A-7 notes).
    /// </summary>
    public static string SanitizeOrg(Guid orgId) => orgId == Guid.Empty ? null : $"org-{orgId:N}";

    /// <summary>
    /// Chromium user-data-dir (persistent profile) for a given org + account label.
    /// Keying on the org first (WP6A-7) means two tenants that both leave <c>AccountLabel</c>
    /// at its "default" default can no longer resolve to the same on-disk profile once a real
    /// org id is known — which today is only the interactive login flow
    /// (<see cref="Endpoints.LinkedInLoginEndpoints"/>); the production per-agent path has no
    /// org id available to it yet and keeps resolving under the unscoped bucket.
    /// </summary>
    public static string ProfileDir(Guid orgId, string accountLabel)
    {
        var orgSegment = SanitizeOrg(orgId);
        return orgSegment is null
            ? Path.Combine(DataDir(), SanitizeAccount(accountLabel), "profile")
            : Path.Combine(DataDir(), orgSegment, SanitizeAccount(accountLabel), "profile");
    }

    // ---- WP6A-7 re-review blocking #1: profile-ownership marker ----
    //
    // LinkedInLoginSession deliberately authenticates every org into the SAME on-disk profile
    // (ProfileDir(Guid.Empty, accountLabel), via LinkedInLoginSession.ProfileOrgId) so the login
    // flow and LinkedInCommand.DoWork's production path — which has no caller org id available to
    // it — always resolve to the identical bucket (see LinkedInLoginSession.ProfileOrgId's doc).
    // That, by itself, means two different orgs that both leave AccountLabel at its shared
    // "default" would silently authenticate into (and drive) the exact same LinkedIn identity.
    //
    // This marker is the guard for that: the first org to successfully authenticate a given
    // profile directory stamps it with its own org id, and IsProfileOwnedByAnotherOrg refuses a
    // later /login/start from any OTHER resolved org for that same directory, forcing it to pick
    // a distinct AccountLabel instead. It intentionally never blocks when either side is
    // Guid.Empty (tenancy disabled, or no prior owner recorded), which preserves prior behavior
    // for single-tenant/dev deployments.

    private const string OwnerMarkerFileName = ".hq-owner-org";

    /// <summary>Path to the ownership marker file inside a resolved profile directory.</summary>
    public static string OwnerMarkerPath(string profileDir) => Path.Combine(profileDir, OwnerMarkerFileName);

    /// <summary>
    /// True only when a profile directory has already been authenticated by a different,
    /// resolved (non-empty) organization than the caller. Never blocks when the caller's own org
    /// is unresolved (tenancy disabled) or when no owner has been recorded yet.
    /// </summary>
    public static bool IsProfileOwnedByAnotherOrg(Guid callerOrgId, Guid? recordedOwnerOrgId) =>
        callerOrgId != Guid.Empty
        && recordedOwnerOrgId.HasValue
        && recordedOwnerOrgId.Value != Guid.Empty
        && recordedOwnerOrgId.Value != callerOrgId;

    /// <summary>
    /// Reads the org id recorded as owning a profile directory, or null if none is recorded yet
    /// (never existed, or its content isn't a valid GUID). Never throws — a missing/unreadable
    /// marker is treated the same as "no prior owner" so IO issues never block the login flow.
    /// </summary>
    public static Guid? ReadProfileOwner(string profileDir)
    {
        try
        {
            var path = OwnerMarkerPath(profileDir);
            if (!File.Exists(path)) return null;
            return Guid.TryParse(File.ReadAllText(path).Trim(), out var orgId) ? orgId : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Records the org id that just successfully authenticated a profile directory. Best-effort:
    /// an IO failure here must never fail (or be mistaken for a security gate on) the login flow
    /// itself — worst case, a later collision goes undetected rather than a successful login
    /// being lost.
    /// </summary>
    public static void WriteProfileOwner(string profileDir, Guid orgId)
    {
        try
        {
            Directory.CreateDirectory(profileDir);
            File.WriteAllText(OwnerMarkerPath(profileDir), orgId.ToString("D"));
        }
        catch
        {
            // Best-effort marker only; never block the login flow on a write failure.
        }
    }

    // ---- WP6A-7 re-review blocking #1 (second pass): atomic pre-launch ownership claim ----
    //
    // The guard above (IsProfileOwnedByAnotherOrg / WriteProfileOwner) only ever consults a
    // marker written AFTER LinkedInLoginSession.Authenticated becomes true. That leaves the
    // entire interactive-login window (Xvfb + Chromium up, user not yet done typing credentials)
    // completely unguarded: a second org calling /login/start against the same (shared,
    // default-label) profile directory while the first org's login is still in flight sees no
    // owner recorded yet, and gets handed a second, concurrent Chromium instance pointed at the
    // IDENTICAL user-data-dir (a TOCTOU, flagged as still-open in re-review). TryClaimProfile
    // closes that window by claiming the marker atomically, via File.Open(..., FileMode.CreateNew)
    // which the OS guarantees fails if another process/thread won the race to create it first,
    // BEFORE any browser is launched — not after authentication succeeds.

    /// <summary>Outcome of <see cref="TryClaimProfile"/>.</summary>
    public enum ProfileClaim
    {
        /// <summary>No marker existed; this call created it and is now the exclusive claimant.</summary>
        ClaimedFresh,

        /// <summary>A marker already existed but recorded this same (or an unresolved) org, so the
        /// caller may proceed exactly as it would have under a fresh claim — the marker is left
        /// untouched rather than rewritten, so it is never mistaken for a claim this call itself
        /// must release.</summary>
        AlreadyOwned,

        /// <summary>A marker already existed and recorded a different, resolved org. The caller
        /// must not launch a browser on this directory.</summary>
        Rejected
    }

    /// <summary>
    /// Atomically claims <paramref name="profileDir"/> for <paramref name="callerOrgId"/> before
    /// any browser is launched on it. Safe to call from multiple threads/processes concurrently:
    /// the underlying <see cref="FileMode.CreateNew"/> open is what the OS serializes, so at most
    /// one concurrent caller can ever observe <see cref="ProfileClaim.ClaimedFresh"/> for a given
    /// directory. Never blocks a caller whose own org is unresolved (<see cref="Guid.Empty"/>,
    /// i.e. tenancy disabled) or who already owns the marker — see <see cref="ProfileClaim"/>.
    /// </summary>
    public static ProfileClaim TryClaimProfile(Guid callerOrgId, string profileDir)
    {
        Directory.CreateDirectory(profileDir);
        var path = OwnerMarkerPath(profileDir);
        try
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(callerOrgId.ToString("D"));
            }
            return ProfileClaim.ClaimedFresh;
        }
        catch (IOException)
        {
            // Someone else won the race to create it (or it already existed from a prior,
            // completed login) — fall back to reading who holds it.
        }

        var owner = ReadProfileOwner(profileDir);
        if (owner is null)
        {
            // Marker exists but is unreadable/unparsable — could be a genuine race with another
            // claimant's write still in flight. Treat conservatively as contested rather than
            // silently allowing a second browser onto the directory.
            return ProfileClaim.Rejected;
        }

        return IsProfileOwnedByAnotherOrg(callerOrgId, owner) ? ProfileClaim.Rejected : ProfileClaim.AlreadyOwned;
    }

    /// <summary>
    /// Releases a claim this caller made with <see cref="TryClaimProfile"/> when the login it was
    /// guarding never reached <c>Authenticated</c> (cancelled, failed, or timed out). Without
    /// this, a single interrupted login attempt would permanently squat on an account label —
    /// including for its own org's later retries. Best-effort: an IO failure here never throws,
    /// since a stuck claim only degrades to "pick a different account label", never a security
    /// hole.
    /// </summary>
    public static void ReleaseProfileClaim(string profileDir)
    {
        try
        {
            File.Delete(OwnerMarkerPath(profileDir));
        }
        catch
        {
            // Best-effort release only.
        }
    }
}
