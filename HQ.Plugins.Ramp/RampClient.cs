using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HQ.Plugins.Ramp.Models;

namespace HQ.Plugins.Ramp;

/// <summary>
/// Ramp Developer API client. Mints an access token via the OAuth client-credentials grant
/// on first use (Basic clientId:clientSecret), then calls the API. Modeled on QuickBooksClient.
/// </summary>
internal class RampClient : IDisposable
{
    private readonly HttpClient _httpClient = new();
    private readonly ServiceConfig _config;
    private readonly string _baseUrl;
    private string _accessToken;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public RampClient(ServiceConfig config)
    {
        _config = config;
        _baseUrl = config.UseSandbox
            ? "https://demo-api.ramp.com/developer/v1"
            : "https://api.ramp.com/developer/v1";
    }

    private string TokenUrl => $"{_baseUrl}/token";

    private async Task EnsureToken()
    {
        if (_accessToken is not null) return;
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_config.ClientId}:{_config.ClientSecret}"));
        using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["scope"] = _config.Scopes ?? ""
        });

        var resp = await _httpClient.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Ramp token request failed {(int)resp.StatusCode}: {Truncate(text)}");
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
            throw new HttpRequestException($"Ramp API error {(int)resp.StatusCode}: {Truncate(text)}");
        return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<JsonElement>(text, JsonOptions);
    }

    private static string Truncate(string body) => body.Length > 500 ? body[..500] + "…" : body;

    public void Dispose() => _httpClient.Dispose();
}
