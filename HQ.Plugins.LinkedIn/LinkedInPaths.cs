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

    /// <summary>Chromium user-data-dir (persistent profile) for a given account label.</summary>
    public static string ProfileDir(string accountLabel) =>
        Path.Combine(DataDir(), SanitizeAccount(accountLabel), "profile");
}
