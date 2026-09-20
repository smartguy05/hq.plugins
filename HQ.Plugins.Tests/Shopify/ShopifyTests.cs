using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Reflection;
using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Models.Safety;
using HQ.Plugins.Shopify;
using HQ.Plugins.Shopify.Models;

namespace HQ.Plugins.Tests.Shopify;

public class ShopifyTests
{
    private static IEnumerable<MethodInfo> ToolMethods() =>
        typeof(ShopifyService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        m.GetCustomAttribute<HQ.Models.Helpers.ParametersAttribute>() != null);

    [Fact]
    public void AllToolMethods_HaveCompleteAnnotations()
    {
        var methods = ToolMethods().ToList();
        Assert.Equal(10, methods.Count);
        foreach (var m in methods)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.GetCustomAttribute<DisplayAttribute>()?.Name), $"{m.Name} missing Display.Name");
            Assert.False(string.IsNullOrWhiteSpace(m.GetCustomAttribute<DescriptionAttribute>()?.Description), $"{m.Name} missing Description");
            var p = m.GetCustomAttribute<HQ.Models.Helpers.ParametersAttribute>();
            Assert.NotNull(p?.ArgsType);
            var schema = HQ.Models.Helpers.ToolSchemaGenerator.Generate(p!.ArgsType);
            Assert.False(string.IsNullOrWhiteSpace(schema), $"{m.Name} missing Parameters");
            Assert.NotNull(JsonDocument.Parse(schema));
        }
    }

    [Theory]
    [InlineData("list_products")]
    [InlineData("create_product")]
    [InlineData("fulfill_order")]
    [InlineData("create_draft_order")]
    public void ExposesExpectedTool(string toolName) =>
        Assert.Contains(toolName, ToolMethods().Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name));

    // WP6B-10: ShopDomain is a TenantAdmin-set config value interpolated straight into the
    // HTTPS base URL with the X-Shopify-Access-Token header attached — never validated, so
    // an internal/loopback/metadata host reaches HttpClient with the tenant's own token. All
    // of these hosts fail fast (loopback refuses instantly, "hq-postgres" won't resolve for a
    // real deployment, decimal/hex IP literals never even leave Uri parsing) so the request
    // never hangs even before the fix — only the graceful, request-never-sent behavior differs.
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    [InlineData("hq-postgres")]
    [InlineData("2130706433")] // decimal literal for 127.0.0.1
    public async Task ListProducts_RejectsSsrfShopDomain(string maliciousDomain)
    {
        var config = new ServiceConfig
        {
            ShopDomain = maliciousDomain,
            AccessToken = "shpat_test-token",
            ApiVersion = "2025-01"
        };
        var service = new ShopifyService(null, (_, _, _) => Task.CompletedTask);

        var result = await service.ListProducts(config, new ListProductsArgs());

        var successProp = result.GetType().GetProperty("Success");
        Assert.NotNull(successProp);
        Assert.False((bool)successProp!.GetValue(result)!);

        // Assert on *why* it failed, not just that it failed — every one of these hosts is
        // also unreachable in this sandbox, so a bare Success==false would pass even with no
        // SSRF check at all (Guard() catches the resulting HttpRequestException/SocketException
        // just the same). Only the deliberate host-validation rejection says "not allowed";
        // a real connection failure says "refused"/"resolve"/"unreachable" instead.
        var errorProp = result.GetType().GetProperty("Error");
        Assert.NotNull(errorProp);
        var error = (string)errorProp!.GetValue(result)!;
        Assert.Contains("not allowed", error, StringComparison.OrdinalIgnoreCase);
    }

    // Adversarial-review follow-up: a literal IP/hardcoded hostname is the toy case. Real
    // shop domains are hostnames, and the realistic SSRF is a TenantAdmin-set ShopDomain
    // whose DNS record resolves to a private/loopback/link-local/metadata address. Before
    // this fix, UrlGuard.IsNavigable was called with no ResolveHost hook, so any non-literal
    // hostname skipped address validation entirely. Uses the internal ShopifyClient
    // constructor overload (visible via InternalsVisibleTo) to inject a fake resolver —
    // no real DNS lookup or network I/O.
    [Theory]
    [InlineData("169.254.169.254")] // cloud metadata endpoint
    [InlineData("10.1.2.3")]        // RFC1918
    [InlineData("127.0.0.1")]       // loopback
    public void ShopifyClient_RejectsDomainNameThatResolvesToInternalAddress(string internalIp)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new ShopifyClient(
            "attacker-controlled.example",
            "shpat_test-token",
            "2025-01",
            resolveHost: _ => new[] { IPAddress.Parse(internalIp) }));

        Assert.Contains("not allowed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShopifyClient_AllowsDomainNameThatResolvesToPublicAddress()
    {
        using var client = new ShopifyClient(
            "legit-shop.example",
            "shpat_test-token",
            "2025-01",
            resolveHost: _ => new[] { IPAddress.Parse("93.184.216.34") });

        Assert.NotNull(client);
    }

    // Second-round adversarial review: real Shopify shop domains are always public
    // (*.myshopify.com or a verified custom domain), so this is not a practical SSRF
    // regression the way HomeAssistantAssist's is — but ShopifyClient shares the exact same
    // unconditional-RFC1918-reject pattern with no escape hatch. Give it the same
    // AllowedHosts wiring for parity/defense-in-depth, mirroring
    // HQ.Services.Utility.UrlValidationService's AllowedHosts / URL_VALIDATION_ALLOWED_HOSTS
    // precedent (same environment-variable name, so one operator-configured list covers
    // webhooks and both plugins).
    [Fact]
    public void ShopifyClient_AllowsDomainNameThatResolvesToInternalAddress_WhenExplicitlyAllowListed()
    {
        var previous = Environment.GetEnvironmentVariable(ShopifyClient.AllowedHostsEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ShopifyClient.AllowedHostsEnvironmentVariable, "internal-shop.example");

            using var client = new ShopifyClient(
                "internal-shop.example",
                "shpat_test-token",
                "2025-01",
                resolveHost: _ => new[] { IPAddress.Parse("10.1.2.3") });

            Assert.NotNull(client);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ShopifyClient.AllowedHostsEnvironmentVariable, previous);
        }
    }
}
