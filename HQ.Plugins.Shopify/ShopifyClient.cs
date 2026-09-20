using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HQ.Models.Safety;

namespace HQ.Plugins.Shopify;

/// <summary>
/// Thin Shopify Admin REST API client. Modeled on AsanaClient but authenticates with the
/// X-Shopify-Access-Token header (custom-app token) rather than Bearer.
/// </summary>
internal class ShopifyClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Comma-separated allow-list of exact host names, read once per client construction —
    /// the operator escape hatch for a deliberately-configured internal target. Second-round
    /// adversarial review: real Shopify shop domains are always public, so this is not a
    /// practical problem in the way HomeAssistantAssist's identical pattern is, but the guard
    /// below shares the same unconditional-RFC1918-reject behavior with no escape hatch.
    /// Mirrors HQ.Services.Utility.UrlValidationService.AllowedHostsEnvironmentVariable (same
    /// name), so one operator-configured list covers webhooks and both plugins.
    /// </summary>
    public const string AllowedHostsEnvironmentVariable = "URL_VALIDATION_ALLOWED_HOSTS";

    private static IEnumerable<string> ReadAllowedHostsFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable(AllowedHostsEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public ShopifyClient(string shopDomain, string accessToken, string apiVersion)
        : this(shopDomain, accessToken, apiVersion, resolveHost: null)
    {
    }

    /// <summary>
    /// Test seam: pass a custom resolver instead of the default
    /// <see cref="Dns.GetHostAddresses(string)"/>. Internal because production callers
    /// should always get real DNS resolution — unit tests use this to simulate a
    /// domain name that resolves to an internal/loopback/metadata address without
    /// touching the network. Visible to HQ.Plugins.Tests via InternalsVisibleTo.
    /// </summary>
    internal ShopifyClient(string shopDomain, string accessToken, string apiVersion, Func<string, IEnumerable<IPAddress>> resolveHost)
    {
        _baseUrl = $"https://{shopDomain?.TrimEnd('/')}/admin/api/{apiVersion}";

        // WP6B-10: ShopDomain is a TenantAdmin-set config value interpolated straight into
        // the HTTPS base URL, with the X-Shopify-Access-Token header attached below — an
        // internal/loopback/metadata host would turn this into an SSRF primitive that leaks
        // the tenant's own access token. Reject before creating the HttpClient (and before
        // the token is ever attached to a request) instead of finding out from a connection
        // attempt.
        //
        // Adversarial-review follow-up: real shop domains are hostnames, not IPs, and the
        // original fix only rejected literal IPs and a handful of hardcoded hostnames — a
        // domain resolving to a private/loopback/link-local/metadata address sailed through
        // untouched. ResolveHost now wires in real DNS resolution (or the injected test
        // resolver) so every resolved address gets the same check a literal IP would.
        var urlGuardOptions = new UrlGuardOptions
        {
            ResolveHost = resolveHost ?? Dns.GetHostAddresses,
            AllowedHosts = ReadAllowedHostsFromEnvironment()
        };
        if (!UrlGuard.IsNavigable(_baseUrl, out var reason, urlGuardOptions))
        {
            throw new InvalidOperationException($"Shopify URL is not allowed: {reason}");
        }

        // Minor adversarial-review note: ShopDomain is TenantAdmin-trusted content, not
        // attacker-served on the happy path, but UrlGuard's own docs warn that a
        // first-hop-clean URL can still redirect into a blocked address. Disable
        // auto-redirect instead of following (and re-validating) each hop.
        _httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        _httpClient.DefaultRequestHeaders.Add("X-Shopify-Access-Token", accessToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<JsonElement> GetAsync(string path) => SendAsync(HttpMethod.Get, path, null);
    public Task<JsonElement> PostAsync(string path, object body) => SendAsync(HttpMethod.Post, path, body);
    public Task<JsonElement> PutAsync(string path, object body) => SendAsync(HttpMethod.Put, path, body);

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object body)
    {
        using var request = new HttpRequestMessage(method, $"{_baseUrl}{path}");
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _httpClient.SendAsync(request);
        await EnsureSuccess(response);
        var content = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(content) ? default : JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        if (body.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) || body.Contains("<html", StringComparison.OrdinalIgnoreCase))
            body = "[HTML response — likely auth or endpoint issue]";
        else if (body.Length > 500)
            body = body[..500] + "…";
        throw new HttpRequestException($"Shopify API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    public void Dispose() => _httpClient.Dispose();
}
