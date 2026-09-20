using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using HQ.Plugins.Email.Endpoints;
using HQ.Plugins.Email.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace HQ.Plugins.Tests.Email.Endpoints;

/// <summary>
/// WP6B-11: POST /test-account had no agent/tenant check at all (the caller supplies raw
/// connection details, so there is no agentId to resolve a tenant-scoped config from),
/// which let any authenticated user point Imap/Smtp at an internal host:port and read the
/// differentiated auth/TLS/unreachable/timeout message back as a port-scanning oracle.
///
/// These tests exercise the route's private/internal helpers directly via reflection
/// (no InternalsVisibleTo is declared for this project) instead of standing up a full HTTP
/// pipeline with JSON model binding.
/// </summary>
public class EmailEndpointsTests
{
    private static readonly MethodInfo IsHostAllowedMethod =
        typeof(EmailEndpoints).GetMethod("IsHostAllowed", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo BuildTestAccountResultAsyncMethod =
        typeof(EmailEndpoints).GetMethod("BuildTestAccountResultAsync", BindingFlags.NonPublic | BindingFlags.Static)
        ?? typeof(EmailEndpoints).GetMethod("BuildTestAccountResultAsync", BindingFlags.Public | BindingFlags.Static);

    private static bool IsHostAllowed(string host, Func<string, IEnumerable<IPAddress>> resolveHost = null)
    {
        Assert.NotNull(IsHostAllowedMethod);
        var args = new object[] { host, null, resolveHost };
        var result = (bool)IsHostAllowedMethod!.Invoke(null, args)!;
        return result;
    }

    [Theory]
    [InlineData("hq-postgres")]      // internal compose service name from the WP6B-11 repro
    [InlineData("HQ-POSTGRES")]      // case-insensitive
    [InlineData("localhost")]
    [InlineData("foo.localhost")]
    [InlineData("127.0.0.1")]        // loopback literal
    [InlineData("169.254.169.254")]  // cloud metadata endpoint (link-local range)
    [InlineData("10.0.0.5")]         // RFC1918
    [InlineData("172.16.0.5")]       // RFC1918
    [InlineData("192.168.1.5")]      // RFC1918
    [InlineData("0.0.0.0")]
    [InlineData("::1")]              // IPv6 loopback
    public void IsHostAllowed_RejectsInternalDestinations(string host) =>
        Assert.False(IsHostAllowed(host));

    // Fake resolver so this stays deterministic and network-free — none of these hosts
    // are blocked-list/literal-IP hits, so (post-fix) the resolver IS invoked for each.
    [Theory]
    [InlineData("imap.gmail.com")]
    [InlineData("smtp.sendgrid.net")]
    public void IsHostAllowed_AllowsExternalHosts(string host) =>
        Assert.True(IsHostAllowed(host, _ => new[] { IPAddress.Parse("93.184.216.34") }));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void IsHostAllowed_AllowsExternalOrBlankHosts(string host) =>
        Assert.True(IsHostAllowed(host));

    // Adversarial-review follow-up: IsHostAllowed only special-cased IP literals and a
    // fixed hostname list — a resolvable hostname (an internal admin host, or an
    // attacker-owned domain pointed at an internal/metadata address) sailed through
    // unresolved. This is exactly the realistic version of the finding's own repro (a
    // TenantAdmin-controlled Imap/Smtp host). Injects a fake resolver — no real DNS lookup.
    [Theory]
    [InlineData("169.254.169.254")] // cloud metadata endpoint
    [InlineData("10.1.2.3")]        // RFC1918
    [InlineData("127.0.0.1")]       // loopback
    public void IsHostAllowed_RejectsDomainNameThatResolvesToInternalAddress(string internalIp) =>
        Assert.False(IsHostAllowed("attacker-controlled.example", _ => new[] { IPAddress.Parse(internalIp) }));

    [Fact]
    public async Task BuildTestAccountResultAsync_BlanksInternalHosts_AndOmitsDifferentiatedMessage()
    {
        Assert.NotNull(BuildTestAccountResultAsyncMethod);

        // Both hosts are internal/blocked so neither branch performs a real network
        // connection — the test stays fast and deterministic while still exercising the
        // exact WP6B-11 repro shape (an internal Imap host, a loopback Smtp host).
        var account = new EmailParameters
        {
            Imap = "hq-postgres",
            ImapPort = 5432,
            Smtp = "127.0.0.1",
            SmtpPort = 25,
            Username = "attacker",
            Password = "irrelevant"
        };

        var task = (Task<object>)BuildTestAccountResultAsyncMethod!.Invoke(null, new object[] { account })!;
        var result = await task;

        var imap = result.GetType().GetProperty("imap")!.GetValue(result)!;
        var smtp = result.GetType().GetProperty("smtp")!.GetValue(result)!;

        Assert.False((bool)imap.GetType().GetProperty("ok")!.GetValue(imap)!);
        Assert.False((bool)smtp.GetType().GetProperty("ok")!.GetValue(smtp)!);

        // Coarse boolean only: the differentiated auth/TLS/unreachable/timeout message
        // EmailService.DescribeConnectionError produces must never appear in the shape.
        Assert.Null(imap.GetType().GetProperty("message"));
        Assert.Null(smtp.GetType().GetProperty("message"));
        Assert.Null(result.GetType().GetProperty("message"));
    }

    [Fact]
    public void TestAccountRoute_RequiresTenantAdminPolicy()
    {
        var app = WebApplication.CreateBuilder().Build();
        EmailEndpoints.Map(app);

        var routeBuilder = (IEndpointRouteBuilder)app;
        var endpoint = routeBuilder.DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == "/test-account");

        var authData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        Assert.Contains(authData, a => a.Policy == "TenantAdmin");
    }
}
