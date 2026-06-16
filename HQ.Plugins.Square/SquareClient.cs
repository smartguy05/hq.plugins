using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HQ.Plugins.Square;

/// <summary>
/// Thin Square Connect v2 client (Bearer + Square-Version header). Modeled on AsanaClient.
/// Raw REST is used deliberately — the official Square SDK rewrites its surface across major
/// versions, while the REST API is stable.
/// </summary>
internal class SquareClient : IDisposable
{
    private const string Version = "2025-01-23";
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public SquareClient(string accessToken, bool useSandbox)
    {
        _baseUrl = useSandbox ? "https://connect.squareupsandbox.com/v2" : "https://connect.squareup.com/v2";
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        _httpClient.DefaultRequestHeaders.Add("Square-Version", Version);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<JsonElement> GetAsync(string path) => SendAsync(HttpMethod.Get, path, null);
    public Task<JsonElement> PostAsync(string path, object body) => SendAsync(HttpMethod.Post, path, body);

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
        if (body.Length > 500) body = body[..500] + "…";
        throw new HttpRequestException($"Square API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    public void Dispose() => _httpClient.Dispose();
}
