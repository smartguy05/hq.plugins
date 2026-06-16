using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace HQ.Plugins.Zendesk;

/// <summary>
/// Thin Zendesk Support REST v2 client. Modeled on HQ.Plugins.Asana's AsanaClient but uses
/// Basic auth ({email}/token:{apiToken}) and retries once on HTTP 429 honoring Retry-After.
/// </summary>
internal class ZendeskClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public ZendeskClient(string subdomain, string email, string apiToken)
    {
        _baseUrl = $"https://{subdomain}.zendesk.com/api/v2";
        _httpClient = new HttpClient();

        var raw = Encoding.UTF8.GetBytes($"{email}/token:{apiToken}");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HQ.Plugins.Zendesk/1.0");
    }

    private string Url(string path) => path.StartsWith("http") ? path : $"{_baseUrl}{path}";

    public async Task<JsonElement> GetAsync(string path) => await SendAsync(HttpMethod.Get, path, null);
    public async Task<JsonElement> PostAsync(string path, object body) => await SendAsync(HttpMethod.Post, path, body);
    public async Task<JsonElement> PutAsync(string path, object body) => await SendAsync(HttpMethod.Put, path, body);

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object body, bool retried = false)
    {
        using var request = new HttpRequestMessage(method, Url(path));
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonOptions);

        var response = await _httpClient.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.TooManyRequests && !retried)
        {
            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
            await Task.Delay(delay);
            return await SendAsync(method, path, body, retried: true);
        }

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
        throw new HttpRequestException($"Zendesk API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    public void Dispose() => _httpClient.Dispose();
}
