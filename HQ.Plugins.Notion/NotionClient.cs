using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HQ.Plugins.Notion;

/// <summary>Thin Notion API client (Bearer auth + Notion-Version header), modeled on HQ.Plugins.Asana's AsanaClient.</summary>
internal class NotionClient : IDisposable
{
    public const string BaseUrl = "https://api.notion.com/v1";
    public const string DefaultVersion = "2022-06-28";

    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public NotionClient(string accessToken, string version)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        _httpClient.DefaultRequestHeaders.Add("Notion-Version", string.IsNullOrWhiteSpace(version) ? DefaultVersion : version);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static string Url(string path) => path.StartsWith("http") ? path : $"{BaseUrl}{path}";

    public async Task<JsonElement> GetAsync(string path)
    {
        var response = await _httpClient.GetAsync(Url(path));
        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public Task<JsonElement> PostAsync(string path, object body) => SendJson(HttpMethod.Post, path, body);
    public Task<JsonElement> PatchAsync(string path, object body) => SendJson(HttpMethod.Patch, path, body);

    private async Task<JsonElement> SendJson(HttpMethod method, string path, object body)
    {
        using var req = new HttpRequestMessage(method, Url(path))
        {
            Content = JsonContent.Create(body, options: JsonOptions)
        };
        var response = await _httpClient.SendAsync(req);
        await EnsureSuccess(response);
        var content = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(content) ? default : JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 500) body = body[..500] + "…";
        throw new HttpRequestException($"Notion API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    public void Dispose() => _httpClient.Dispose();
}
