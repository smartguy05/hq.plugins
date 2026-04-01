using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HQ.Plugins.Asana;

internal class AsanaClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public AsanaClient(string baseUrl, string accessToken)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient();

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HQ.Plugins.Asana/1.0");
    }

    private string Url(string path) => $"{_baseUrl}{path}";

    public async Task<JsonElement> GetAsync(string path)
    {
        var response = await _httpClient.GetAsync(Url(path));
        await EnsureSuccess(response);
        var root = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        return UnwrapData(root);
    }

    public async Task<JsonElement> GetRawAsync(string path)
    {
        var response = await _httpClient.GetAsync(Url(path));
        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> PostAsync(string path, object body)
    {
        var wrapped = new { data = body };
        var response = await _httpClient.PostAsJsonAsync(Url(path), wrapped, JsonOptions);
        await EnsureSuccess(response);
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return default;
        var root = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
        return UnwrapData(root);
    }

    public async Task<JsonElement> PutAsync(string path, object body)
    {
        var wrapped = new { data = body };
        var response = await _httpClient.PutAsJsonAsync(Url(path), wrapped, JsonOptions);
        await EnsureSuccess(response);
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return default;
        var root = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
        return UnwrapData(root);
    }

    public async Task DeleteAsync(string path)
    {
        var response = await _httpClient.DeleteAsync(Url(path));
        await EnsureSuccess(response);
    }

    private static JsonElement UnwrapData(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var data))
            return data;
        return root;
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            // Truncate HTML responses to avoid dumping entire pages into error messages
            if (body.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) || body.Contains("<html", StringComparison.OrdinalIgnoreCase))
                body = $"[HTML response — likely auth or endpoint issue]";
            else if (body.Length > 500)
                body = body[..500] + "…";
            throw new HttpRequestException(
                $"Asana API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
