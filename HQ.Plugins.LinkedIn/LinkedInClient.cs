using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HQ.Plugins.LinkedIn;

internal class LinkedInClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _personUrn;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public LinkedInClient(string accessToken, string personUrn)
    {
        _personUrn = personUrn;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.linkedin.com")
        };

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        _httpClient.DefaultRequestHeaders.Add("LinkedIn-Version", "202401");
        _httpClient.DefaultRequestHeaders.Add("X-Restli-Protocol-Version", "2.0.0");
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

    public async Task DeleteAsync(string path)
    {
        var response = await _httpClient.DeleteAsync(path);
        await EnsureSuccess(response);
    }

    public string PersonUrn => _personUrn;

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"LinkedIn API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
