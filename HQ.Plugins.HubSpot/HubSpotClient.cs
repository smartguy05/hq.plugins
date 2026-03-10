using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HQ.Plugins.HubSpot;

internal class HubSpotClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public HubSpotClient(string baseUrl, string accessToken)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl)
        };

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
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

    public async Task<JsonElement> PatchAsync(string path, object body)
    {
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await _httpClient.PatchAsync(path, content);
        await EnsureSuccess(response);
        var responseContent = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(responseContent))
            return default;
        return JsonSerializer.Deserialize<JsonElement>(responseContent, JsonOptions);
    }

    public async Task<JsonElement> PutAsync(string path, object body)
    {
        var response = await _httpClient.PutAsJsonAsync(path, body, JsonOptions);
        await EnsureSuccess(response);
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return default;
        return JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
    }

    public async Task DeleteAsync(string path)
    {
        var response = await _httpClient.DeleteAsync(path);
        await EnsureSuccess(response);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"HubSpot API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
