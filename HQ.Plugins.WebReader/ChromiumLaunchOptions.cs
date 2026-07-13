using Microsoft.Playwright;

namespace HQ.Plugins.WebReader;

/// <summary>
/// DEP-01: builds the Chromium launch options for the runtime environment. Inside the
/// hardened container (non-root, cap_drop ALL, no-new-privileges) Chromium's setuid
/// sandbox cannot start, so --no-sandbox is required there; on developer desktops the
/// sandbox stays on. Detection uses DOTNET_RUNNING_IN_CONTAINER (set by the aspnet base
/// images), overridable either way via HQ_CHROMIUM_NO_SANDBOX=true|false.
/// --disable-dev-shm-usage is always applied (container /dev/shm is tiny; harmless on
/// desktops). Pure function of (headless, env) so it is unit-testable without a browser.
/// </summary>
internal static class ChromiumLaunchOptions
{
    public static BrowserTypeLaunchOptions Build(bool headless, Func<string, string> getEnv)
    {
        var args = new List<string> { "--disable-dev-shm-usage" };

        var overrideValue = getEnv("HQ_CHROMIUM_NO_SANDBOX");
        var noSandbox = overrideValue is not null
            ? string.Equals(overrideValue, "true", StringComparison.OrdinalIgnoreCase)
            : string.Equals(getEnv("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);
        if (noSandbox)
            args.Add("--no-sandbox");

        return new BrowserTypeLaunchOptions
        {
            Headless = headless,
            Args = args
        };
    }

    public static BrowserTypeLaunchOptions Build(bool headless) =>
        Build(headless, Environment.GetEnvironmentVariable);
}
