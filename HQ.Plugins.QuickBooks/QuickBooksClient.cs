using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HQ.Plugins.QuickBooks.Models;

namespace HQ.Plugins.QuickBooks;

/// <summary>
/// QuickBooks Online Accounting API v3 client. Exchanges the stored refresh token for an
/// access token on first use (Intuit token endpoint), then calls the company API. Modeled on
/// HQ.Plugins.Asana's AsanaClient but with the Intuit OAuth refresh step and Realm-scoped paths.
/// </summary>
internal class QuickBooksClient : IDisposable
{
    private const string TokenUrl = "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer";
    private const int MinorVersion = 70;

    private readonly HttpClient _httpClient = new();
    private readonly IntuitCredentials _creds;
    private readonly string _realmId;
    private readonly string _baseUrl;
    private string _accessToken;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public QuickBooksClient(ServiceConfig config)
    {
        _creds = config.Credentials ?? throw new InvalidOperationException("QuickBooks credentials are not configured.");
        _realmId = string.IsNullOrWhiteSpace(config.RealmId)
            ? throw new InvalidOperationException("RealmId is not configured.")
            : config.RealmId;
        _baseUrl = config.UseSandbox
            ? "https://sandbox-quickbooks.api.intuit.com"
            : "https://quickbooks.api.intuit.com";
    }

    private async Task EnsureToken()
    {
        if (_accessToken is not null) return;

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_creds.ClientId}:{_creds.ClientSecret}"));
        using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = _creds.RefreshToken
        });

        var resp = await _httpClient.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Intuit token refresh failed {(int)resp.StatusCode}: {Truncate(text)}");

        _accessToken = JsonDocument.Parse(text).RootElement.GetProperty("access_token").GetString();
    }

    private string CompanyUrl(string path) => $"{_baseUrl}/v3/company/{_realmId}{path}";

    private async Task<HttpRequestMessage> Authed(HttpMethod method, string url)
    {
        await EnsureToken();
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return req;
    }

    /// <summary>Runs a QBO SQL query (e.g. "SELECT * FROM Customer MAXRESULTS 20").</summary>
    public async Task<JsonElement> QueryAsync(string sql)
    {
        var url = CompanyUrl($"/query?minorversion={MinorVersion}&query={Uri.EscapeDataString(sql)}");
        using var req = await Authed(HttpMethod.Get, url);
        return await Send(req);
    }

    public async Task<JsonElement> GetAsync(string path)
    {
        var sep = path.Contains('?') ? "&" : "?";
        using var req = await Authed(HttpMethod.Get, CompanyUrl($"{path}{sep}minorversion={MinorVersion}"));
        return await Send(req);
    }

    public async Task<JsonElement> PostAsync(string path, object body)
    {
        var sep = path.Contains('?') ? "&" : "?";
        using var req = await Authed(HttpMethod.Post, CompanyUrl($"{path}{sep}minorversion={MinorVersion}"));
        req.Content = JsonContent.Create(body, options: JsonOptions);
        return await Send(req);
    }

    private async Task<JsonElement> Send(HttpRequestMessage req)
    {
        var resp = await _httpClient.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"QuickBooks API error {(int)resp.StatusCode}: {Truncate(text)}");
        return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<JsonElement>(text, JsonOptions);
    }

    private static string Truncate(string body)
    {
        if (body.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) || body.Contains("<html", StringComparison.OrdinalIgnoreCase))
            return "[HTML response — likely auth or endpoint issue]";
        return body.Length > 500 ? body[..500] + "…" : body;
    }

    public void Dispose() => _httpClient.Dispose();
}
