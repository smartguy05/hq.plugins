using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.HeadlessBrowser.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Run browser in headless mode (default true). Set false for debugging.")]
    public bool Headless { get; set; } = true;

    [Tooltip("Default navigation timeout in milliseconds (default 30000).")]
    public int DefaultTimeoutMs { get; set; } = 30000;

    [Tooltip("Custom user agent string. Leave blank for default Chromium UA.")]
    public string UserAgent { get; set; }

    [Tooltip("Browser viewport width in pixels (default 1280).")]
    public int ViewportWidth { get; set; } = 1280;

    [Tooltip("Browser viewport height in pixels (default 720).")]
    public int ViewportHeight { get; set; } = 720;

    [Tooltip("Directory to save screenshots. Defaults to temp directory.")]
    public string ScreenshotDirectory { get; set; }
}
