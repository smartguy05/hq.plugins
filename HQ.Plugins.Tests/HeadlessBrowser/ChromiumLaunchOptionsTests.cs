using HQ.Plugins.HeadlessBrowser;

namespace HQ.Plugins.Tests.HeadlessBrowser;

/// <summary>
/// DEP-01: inside the hardened container (non-root, cap_drop ALL, no-new-privileges) the
/// Chromium setuid sandbox cannot start, so the launch options must add --no-sandbox when
/// running in a container — while keeping the sandbox ON for developer desktops.
/// --disable-dev-shm-usage is safe everywhere (containers have a tiny /dev/shm).
/// </summary>
public class ChromiumLaunchOptionsTests
{
    private static string ContainerEnv(string name) =>
        name == "DOTNET_RUNNING_IN_CONTAINER" ? "true" : null;

    private static string DesktopEnv(string name) => null;

    [Fact]
    public void Build_InContainer_AddsNoSandboxAndDisableDevShmUsage()
    {
        var options = ChromiumLaunchOptions.Build(headless: true, ContainerEnv);
        Assert.Contains("--no-sandbox", options.Args);
        Assert.Contains("--disable-dev-shm-usage", options.Args);
        Assert.Contains("--crash-dumps-dir=/tmp", options.Args);
    }

    [Fact]
    public void Build_OutsideContainer_OmitsNoSandbox_KeepsDisableDevShm()
    {
        var options = ChromiumLaunchOptions.Build(headless: true, DesktopEnv);
        Assert.DoesNotContain("--no-sandbox", options.Args);
        Assert.DoesNotContain("--crash-dumps-dir=/tmp", options.Args);
        Assert.Contains("--disable-dev-shm-usage", options.Args);
    }

    [Fact]
    public void Build_EnvOverrideTrue_ForcesNoSandbox()
    {
        var options = ChromiumLaunchOptions.Build(headless: true,
            name => name == "HQ_CHROMIUM_NO_SANDBOX" ? "true" : null);
        Assert.Contains("--no-sandbox", options.Args);
    }

    [Fact]
    public void Build_EnvOverrideFalse_SuppressesNoSandbox_EvenInContainer()
    {
        var options = ChromiumLaunchOptions.Build(headless: true,
            name => name switch
            {
                "HQ_CHROMIUM_NO_SANDBOX" => "false",
                "DOTNET_RUNNING_IN_CONTAINER" => "true",
                _ => null
            });
        Assert.DoesNotContain("--no-sandbox", options.Args);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Build_PreservesHeadlessFlag(bool headless)
    {
        var options = ChromiumLaunchOptions.Build(headless, DesktopEnv);
        Assert.Equal(headless, options.Headless);
    }
}
