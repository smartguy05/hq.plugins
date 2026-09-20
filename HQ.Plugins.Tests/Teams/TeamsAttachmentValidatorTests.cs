using System.Net;
using HQ.Plugins.Teams;

namespace HQ.Plugins.Tests.Teams;

/// <summary>
/// WP6B-8: TeamsBot.OnMessageActivityAsync downloaded any inbound attachment's `contentUrl`
/// with a bare HttpClient -- no scheme check, no host allow-list, no protection against a
/// literal internal/loopback/cloud-metadata IP or a hostname whose DNS record resolves to one.
/// A crafted Activity with attachments[0].contentUrl pointing at
/// http://169.254.169.254/latest/meta-data/ (cloud metadata) or an internal service address was
/// downloaded, base64'd, and put into the agent prompt (SSRF, with the response readable by the
/// caller through the agent). TeamsAttachmentValidator delegates to HQ.Models.Safety.UrlGuard
/// (deny-by-default: only http/https, blocks loopback/link-local/RFC1918/metadata addresses,
/// and -- via the injected ResolveHost hook -- resolves hostnames before deciding, so a
/// DNS-rebinding-style hostname is caught too, not just a raw IP literal).
/// </summary>
public class TeamsAttachmentValidatorTests
{
    private static Func<string, IEnumerable<IPAddress>> Resolves(params string[] ips) =>
        _ => ips.Select(IPAddress.Parse);

    [Fact]
    public void IsAttachmentUrlAllowed_RejectsCloudMetadataAddress()
    {
        var allowed = TeamsAttachmentValidator.IsAttachmentUrlAllowed(
            "http://169.254.169.254/latest/meta-data/", out var reason);

        Assert.False(allowed);
        Assert.NotNull(reason);
    }

    [Fact]
    public void IsAttachmentUrlAllowed_RejectsPrivateRfc1918Address()
    {
        var allowed = TeamsAttachmentValidator.IsAttachmentUrlAllowed(
            "http://192.168.1.8:8080/api/messages", out var reason);

        Assert.False(allowed);
        Assert.NotNull(reason);
    }

    [Fact]
    public void IsAttachmentUrlAllowed_RejectsLoopback()
    {
        var allowed = TeamsAttachmentValidator.IsAttachmentUrlAllowed(
            "http://127.0.0.1:5000/secrets", out var reason);

        Assert.False(allowed);
    }

    [Fact]
    public void IsAttachmentUrlAllowed_RejectsNonHttpScheme()
    {
        var allowed = TeamsAttachmentValidator.IsAttachmentUrlAllowed(
            "file:///etc/passwd", out var reason);

        Assert.False(allowed);
    }

    [Fact]
    public void IsAttachmentUrlAllowed_RejectsNullOrEmpty()
    {
        Assert.False(TeamsAttachmentValidator.IsAttachmentUrlAllowed(null, out _));
        Assert.False(TeamsAttachmentValidator.IsAttachmentUrlAllowed("", out _));
    }

    [Fact]
    public void IsAttachmentUrlAllowed_AllowsOrdinaryPublicHttpsUrl()
    {
        // A resolver is injected (returning a public IP) so this test does no real DNS I/O; it
        // still exercises the same "resolves to a navigable address" path production takes.
        var allowed = TeamsAttachmentValidator.IsAttachmentUrlAllowed(
            "https://contoso.sharepoint.com/sites/team/Shared%20Documents/file.docx",
            out var reason,
            Resolves("52.96.0.10"));

        Assert.True(allowed);
        Assert.Null(reason);
    }

    [Fact]
    public void IsAttachmentUrlAllowed_WithResolveHost_RejectsHostnameThatResolvesToPrivateIp()
    {
        // DNS-rebinding-style case: the hostname itself looks external, but its A record points
        // inside the network. Only reachable through the resolveHost seam (no real DNS lookups
        // in unit tests).
        var allowed = TeamsAttachmentValidator.IsAttachmentUrlAllowed(
            "http://attacker-controlled.example.com/file",
            out var reason,
            Resolves("10.0.0.5"));

        Assert.False(allowed);
        Assert.NotNull(reason);
    }

    /// <summary>
    /// WP6B-8 review follow-up: the original fix validated only the literal contentUrl before
    /// the fetch; a plain HttpClient's default AllowAutoRedirect=true would then follow a 302
    /// from an allowed public host straight into an internal/cloud-metadata address, bypassing
    /// the check (TOCTOU via redirect). IsRedirectAllowed re-applies the same deny-by-default
    /// rules to a Location header/hop so a caller that disables auto-redirect can validate every
    /// hop, not just the first.
    /// </summary>
    [Fact]
    public void IsRedirectAllowed_AllowsRedirectToOrdinaryPublicUrl()
    {
        var current = new Uri("https://attacker.example/redirect");

        var allowed = TeamsAttachmentValidator.IsRedirectAllowed(
            current, "https://contoso.sharepoint.com/file.docx", out var target, out var reason,
            Resolves("52.96.0.10"));

        Assert.True(allowed);
        Assert.Null(reason);
        Assert.Equal("https://contoso.sharepoint.com/file.docx", target.ToString());
    }

    [Fact]
    public void IsRedirectAllowed_RejectsRedirectToCloudMetadataAddress()
    {
        var current = new Uri("https://attacker.example/redirect");

        var allowed = TeamsAttachmentValidator.IsRedirectAllowed(
            current, "http://169.254.169.254/latest/meta-data/", out _, out var reason);

        Assert.False(allowed);
        Assert.NotNull(reason);
    }

    [Fact]
    public void IsRedirectAllowed_RejectsRedirectToHostnameThatResolvesToPrivateIp()
    {
        var current = new Uri("https://attacker.example/redirect");

        var allowed = TeamsAttachmentValidator.IsRedirectAllowed(
            current, "http://internal.example/secrets", out _, out var reason,
            Resolves("192.168.1.8"));

        Assert.False(allowed);
        Assert.NotNull(reason);
    }

    [Fact]
    public void IsRedirectAllowed_RejectsEmptyLocation()
    {
        var current = new Uri("https://attacker.example/redirect");

        Assert.False(TeamsAttachmentValidator.IsRedirectAllowed(current, "", out _, out _));
        Assert.False(TeamsAttachmentValidator.IsRedirectAllowed(current, null, out _, out _));
    }
}
