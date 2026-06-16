using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HQ.Plugins.Health;

/// <summary>Thin Terra API client (dev-id + x-api-key headers).</summary>
internal class TerraClient : IDisposable
{
    public const string BaseUrl = "https://api.tryterra.co/v2";

    private readonly HttpClient _httpClient;

    public TerraClient(string devId, string apiKey)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("dev-id", devId);
        _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<JsonElement> GetAsync(string path)
    {
        var response = await _httpClient.GetAsync($"{BaseUrl}{path}");
        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 600) body = body[..600] + "…";
        throw new HttpRequestException($"Terra API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    public void Dispose() => _httpClient.Dispose();
}
