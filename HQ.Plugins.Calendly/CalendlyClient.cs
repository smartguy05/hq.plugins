using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HQ.Plugins.Calendly;

/// <summary>Thin Calendly API v2 client (Bearer auth), modeled on HQ.Plugins.Asana's AsanaClient.</summary>
internal class CalendlyClient : IDisposable
{
    public const string BaseUrl = "https://api.calendly.com";
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public CalendlyClient(string accessToken)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static string Url(string path) => path.StartsWith("http") ? path : $"{BaseUrl}{path}";

    public async Task<JsonElement> GetAsync(string path)
    {
        var response = await _httpClient.GetAsync(Url(path));
        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> PostAsync(string path, object body)
    {
        var response = await _httpClient.PostAsJsonAsync(Url(path), body, JsonOptions);
        await EnsureSuccess(response);
        var content = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(content) ? default : JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 500) body = body[..500] + "…";
        throw new HttpRequestException($"Calendly API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    public void Dispose() => _httpClient.Dispose();
}
