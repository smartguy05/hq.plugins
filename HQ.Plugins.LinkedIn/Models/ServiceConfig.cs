using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.LinkedIn.Models;

/// <summary>
/// Configuration for the browser-driven LinkedIn plugin. There is intentionally
/// <b>no password and no API key here</b> — the LinkedIn session is captured once
/// through the interactive login window and lives in an on-disk Chromium profile
/// (see <see cref="LinkedInPaths"/>), never in this config JSON. The only secret a
/// caller may optionally paste is a full Playwright <c>storageState</c> JSON for the
/// fallback login path; it is marked <see cref="SensitiveAttribute"/>.
/// </summary>
public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Friendly label for the connected LinkedIn account (e.g. 'Primary'). Also keys the on-disk session profile so multiple accounts don't collide.")]
    public string AccountLabel { get; set; } = "default";

    [Tooltip("Run the steady-state browser headless. Leave false (headed under a virtual display) — LinkedIn flags headless Chromium more aggressively.")]
    public bool Headless { get; set; } = false;

    [Tooltip("Require an explicit confirmation before any outbound write (message, post, comment, reaction, invitation). Strongly recommended for a primary account.")]
    public bool RequiresConfirmation { get; set; } = true;

    // ---- Anti-detection hygiene (kept stable across sessions to build a trust trail) ----

    [Tooltip("User-Agent string for the session. Leave blank to use Chromium's default.")]
    public string UserAgent { get; set; }

    [Tooltip("Browser locale, e.g. 'en-US'.")]
    public string Locale { get; set; } = "en-US";

    [Tooltip("Browser timezone, e.g. 'America/New_York'. Keep consistent with your real location.")]
    public string TimezoneId { get; set; }

    // ---- Rate limiting (per UTC day). Conservative defaults for a real account. ----

    [Tooltip("Max connection invitations to send per day.")]
    public int MaxInvitationsPerDay { get; set; } = 20;

    [Tooltip("Max messages (send_message + start_new_chat) per day.")]
    public int MaxMessagesPerDay { get; set; } = 40;

    [Tooltip("Max profile/people/company search & lookup calls per day (highest detection risk).")]
    public int MaxSearchesPerDay { get; set; } = 80;

    [Tooltip("Optional full Playwright storageState JSON — fallback to seed a session without the interactive login window. Leave blank to use the interactive login.")]
    [Sensitive]
    public string StorageStateJson { get; set; }
}
