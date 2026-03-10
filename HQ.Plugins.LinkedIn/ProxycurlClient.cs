using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HQ.Plugins.LinkedIn;

internal class ProxycurlClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ProxycurlClient(string baseUrl, string apiKey)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl)
        };

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<JsonElement> GetAsync(string path)
    {
        var response = await _httpClient.GetAsync(path);
        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> PostAsync(string path, object body)
    {
        var response = await _httpClient.PostAsJsonAsync(path, body, JsonOptions);
        await EnsureSuccess(response);
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return default;
        return JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Proxycurl API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
