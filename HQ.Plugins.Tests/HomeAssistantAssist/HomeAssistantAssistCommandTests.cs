using System.Net;
using System.Reflection;
using HQ.Plugins.HomeAssistantVoice;
using HQ.Plugins.HomeAssistantVoice.Models;

namespace HQ.Plugins.Tests.HomeAssistantAssist;

/// <summary>
/// WP6B-10: HomeAssistUrl is a TenantAdmin-set config value with no SSRF validation
/// before the plugin attaches the long-lived HA bearer token and POSTs to it. These
/// tests exercise the public tool method directly (no HTTP mocking needed) — a
/// blocked URL must be rejected before any HttpClient request is attempted, so the
/// call returns quickly and gracefully instead of doing (or failing) real I/O.
///
/// The chosen hosts all fail fast even without the fix (loopback refuses instantly,
/// "hq-postgres" won't resolve in a real deployment's DNS, file:// never touches the
/// network) — deliberately avoiding any RFC1918/link-local literal that could sit on
/// an ARP timeout in a real network namespace and make the test hang.
/// </summary>
public class HomeAssistantAssistCommandTests
{
    private static async Task<object> InvokeAsync(string url, Func<string, IEnumerable<IPAddress>> resolveHost = null)
    {
        var command = new HomeAssistantAssistCommand
        {
            // Normally set by CommandBase.Execute() before DoWork runs; set directly
            // here since the test calls the tool method straight, bypassing Execute().
            Logger = (_, _, _) => Task.CompletedTask,
            ResolveHostForTesting = resolveHost
        };
        var config = new ServiceConfig
        {
            HomeAssistApiKey = "long-lived-token",
            HomeAssistUrl = url
        };
        return await command.HomeAssistantCommand(config, new HomeAssistantCommandArgs { Query = "turn on the lights" });
    }

    private static bool GetSuccess(object result)
    {
        var prop = result.GetType().GetProperty("Success", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(prop);
        return (bool)prop!.GetValue(result)!;
    }

    [Theory]
    [InlineData("http://127.0.0.1:1/api/conversation/process")]
    [InlineData("http://localhost:1/api/conversation/process")]
    [InlineData("http://hq-postgres/api/conversation/process")]
    public async Task HomeAssistantCommand_RejectsSsrfUrl_Gracefully(string maliciousUrl)
    {
        // Before the fix this either throws (unhandled HttpRequestException/SocketException
        // from the real connection attempt) or, at best, would only fail incidentally
        // because nothing is listening — not because the URL was recognized as unsafe.
        var result = await InvokeAsync(maliciousUrl);

        Assert.False(GetSuccess(result));
    }

    [Fact]
    public async Task HomeAssistantCommand_RejectsNonHttpScheme_WithoutThrowing()
    {
        var result = await InvokeAsync("file:///etc/passwd");

        Assert.False(GetSuccess(result));
    }

    // Adversarial-review follow-up: a literal IP/hardcoded hostname is the toy case. The
    // realistic SSRF here is a TenantAdmin pointing HomeAssistUrl at an attacker-controlled
    // domain name whose DNS record resolves to an internal/metadata address. Before this
    // fix, UrlGuard.IsNavigable was called with no ResolveHost hook, so any non-literal
    // hostname skipped address validation entirely — this test injects a fake resolver
    // (no real DNS lookup) to prove that gap is closed without depending on network access.
    [Theory]
    [InlineData("169.254.169.254")] // cloud metadata endpoint
    [InlineData("10.1.2.3")]        // RFC1918
    public async Task HomeAssistantCommand_RejectsDomainNameThatResolvesToInternalAddress(string internalIp)
    {
        var result = await InvokeAsync(
            "http://attacker-controlled.example/api/conversation/process",
            resolveHost: _ => new[] { IPAddress.Parse(internalIp) });

        Assert.False(GetSuccess(result));
    }

    [Fact]
    public void HomeAssistantCommand_DomainNameResolvingToPublicAddress_IsNavigable()
    {
        // Sanity check for the theory above, at the guard level only (no real HTTP call —
        // an actual connection attempt to a public IP could hang for the OS TCP-connect
        // timeout in a sandbox with no network egress): a legitimate external hostname
        // resolving to a public address must not be rejected by the new DNS check.
        var options = new HQ.Models.Safety.UrlGuardOptions
        {
            ResolveHost = _ => new[] { IPAddress.Parse("93.184.216.34") }
        };

        Assert.True(HQ.Models.Safety.UrlGuard.IsNavigable(
            "http://legit-home-assistant.example/api/conversation/process", out _, options));
    }

    // Second-round adversarial review: UrlGuard.IsAddressNavigable unconditionally rejects
    // ALL of RFC1918 (10/8, 172.16/12, 192.168/16) with no escape hatch — but Home Assistant's
    // own ServiceConfig.cs Tooltip gives "http://192.168.1.100:8123" as the canonical example,
    // and running it on a private LAN (or a same-docker-network container) is the plugin's
    // primary real-world deployment, not an edge case. The fix above closed the SSRF bypass
    // but, with no AllowedHosts wired in, also permanently disabled the plugin for essentially
    // every real user. These tests prove the escape-hatch wiring (mirroring
    // HQ.Services.Utility.UrlValidationService's AllowedHosts / URL_VALIDATION_ALLOWED_HOSTS
    // precedent, built for exactly this "self-hosted target" case) actually reaches the guard
    // the command builds, without making a real network call.
    [Fact]
    public void BuildUrlGuardOptions_AllowsExplicitlyAllowListedPrivateHost()
    {
        var command = new HomeAssistantAssistCommand
        {
            AllowedHostsForTesting = new[] { "192.168.1.100" }
        };

        var options = command.BuildUrlGuardOptionsForTesting();

        Assert.True(HQ.Models.Safety.UrlGuard.IsNavigable(
            "http://192.168.1.100:8123/api/conversation/process", out var reason, options));
        Assert.Null(reason);
    }

    [Fact]
    public void BuildUrlGuardOptions_StillRejectsPrivateHost_WhenNotAllowListed()
    {
        var command = new HomeAssistantAssistCommand
        {
            AllowedHostsForTesting = new[] { "192.168.1.200" } // a different host
        };

        var options = command.BuildUrlGuardOptionsForTesting();

        Assert.False(HQ.Models.Safety.UrlGuard.IsNavigable(
            "http://192.168.1.100:8123/api/conversation/process", out var reason, options));
        Assert.NotNull(reason);
    }

    [Fact]
    public void BuildUrlGuardOptions_ReadsAllowedHostsFromEnvironmentVariable_WhenNoTestOverrideSet()
    {
        var previous = Environment.GetEnvironmentVariable(HomeAssistantAssistCommand.AllowedHostsEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                HomeAssistantAssistCommand.AllowedHostsEnvironmentVariable, "192.168.1.100, my-ha.local");

            var command = new HomeAssistantAssistCommand();
            var options = command.BuildUrlGuardOptionsForTesting();

            Assert.Contains("192.168.1.100", options.AllowedHosts);
            Assert.Contains("my-ha.local", options.AllowedHosts);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                HomeAssistantAssistCommand.AllowedHostsEnvironmentVariable, previous);
        }
    }
}
