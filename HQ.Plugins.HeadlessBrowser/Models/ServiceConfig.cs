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
    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    [Tooltip("Browser viewport width in pixels (default 1280).")]
    public int ViewportWidth { get; set; } = 1280;

    [Tooltip("Browser viewport height in pixels (default 720).")]
    public int ViewportHeight { get; set; } = 720;

    [Tooltip("Directory to save screenshots. Defaults to temp directory.")]
    public string ScreenshotDirectory { get; set; }

    [Tooltip("Maximum lines to return from AriaSnapshot (default 300). Reduces token usage on large pages.")]
    public int MaxSnapshotLines { get; set; } = 300;

    [Tooltip("Use Playwright AriaSnapshot for page content (default true). Falls back to innerText if snapshot is too sparse.")]
    public bool PreferAriaSnapshot { get; set; } = true;

    [Tooltip("Maximum characters per text node in DOM compression (default 100).")]
    public int TextTruncationLimit { get; set; } = 100;

    [Tooltip("SimHash similarity threshold percentage for list folding (default 60).")]
    public int SimHashSimilarityThreshold { get; set; } = 60;

    [Tooltip("Collapse repeating sibling structures into a single example (default true).")]
    public bool EnableListFolding { get; set; } = true;
}
