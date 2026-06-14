using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

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

    public ShopifyClient(string shopDomain, string accessToken, string apiVersion)
    {
        _baseUrl = $"https://{shopDomain.TrimEnd('/')}/admin/api/{apiVersion}";
        _httpClient = new HttpClient();
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
