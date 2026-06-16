using System.Net.Http.Json;
using System.Text.Json;

namespace HQ.Plugins.Maps;

/// <summary>Thin Google Maps Platform client (API-key auth, JSON web-service endpoints).</summary>
internal class MapsClient : IDisposable
{
    public const string BaseUrl = "https://maps.googleapis.com/maps/api";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public MapsClient(string apiKey)
    {
        _apiKey = apiKey;
        _httpClient = new HttpClient();
    }

    /// <summary>GET a Maps endpoint path (with query, minus key). The API key is appended automatically.</summary>
    public async Task<JsonElement> GetAsync(string pathWithQuery)
    {
        var sep = pathWithQuery.Contains('?') ? "&" : "?";
        var url = $"{BaseUrl}{pathWithQuery}{sep}key={_apiKey}";
        var response = await _httpClient.GetAsync(url);
        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 500) body = body[..500] + "…";
        throw new HttpRequestException($"Google Maps API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    public void Dispose() => _httpClient.Dispose();
}
