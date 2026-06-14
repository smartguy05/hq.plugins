using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HQ.Plugins.Gusto.Models;

namespace HQ.Plugins.Gusto;

/// <summary>
/// Gusto API v1 client. Exchanges the stored refresh token for an access token on first use,
/// then calls the API. Modeled on QuickBooksClient. Demo vs production selected by config.
/// </summary>
internal class GustoClient : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly ServiceConfig _config;
    private readonly string _baseUrl;
    private string _accessToken;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public GustoClient(ServiceConfig config)
    {
        _config = config;
        _baseUrl = config.UseDemo ? "https://api.gusto-demo.com" : "https://api.gusto.com";
    }

    private async Task EnsureToken()
    {
        if (_accessToken is not null) return;
        var creds = _config.Credentials ?? throw new InvalidOperationException("Gusto credentials are not configured.");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/oauth/token");
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = creds.RefreshToken,
            ["client_id"] = creds.ClientId,
            ["client_secret"] = creds.ClientSecret
        });

        var resp = await _httpClient.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Gusto token refresh failed {(int)resp.StatusCode}: {Truncate(text)}");
        _accessToken = JsonDocument.Parse(text).RootElement.GetProperty("access_token").GetString();
    }

    public async Task<JsonElement> GetAsync(string path)
    {
        await EnsureToken();
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{path}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var resp = await _httpClient.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Gusto API error {(int)resp.StatusCode}: {Truncate(text)}");
        return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<JsonElement>(text, JsonOptions);
    }

    private static string Truncate(string body) => body.Length > 500 ? body[..500] + "…" : body;

    public void Dispose() => _httpClient.Dispose();
}
