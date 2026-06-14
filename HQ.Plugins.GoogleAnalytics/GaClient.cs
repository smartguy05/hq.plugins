using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using HQ.Plugins.GoogleAnalytics.Models;

namespace HQ.Plugins.GoogleAnalytics;

/// <summary>
/// Calls the GA4 Data API over REST using a refresh-token Google credential (same OAuth setup
/// as GoogleWorkspace/CalService). The .NET GA SDK is beta-only, so REST keeps this stable.
/// </summary>
public class GaClient : IDisposable
{
    private const string DataBase = "https://analyticsdata.googleapis.com/v1beta";
    private const string Scope = "https://www.googleapis.com/auth/analytics.readonly";

    private readonly HttpClient _httpClient = new();
    private readonly UserCredential _credential;

    public GaClient(ServiceConfig config)
    {
        var creds = config.Credentials ?? throw new InvalidOperationException("Google Analytics credentials are not configured.");
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = creds.ClientId, ClientSecret = creds.ClientSecret },
            Scopes = [Scope]
        });
        _credential = new UserCredential(flow, creds.GoogleUser ?? "user", new TokenResponse { RefreshToken = creds.RefreshToken });
    }

    private async Task<HttpRequestMessage> Authed(HttpMethod method, string url)
    {
        var token = await _credential.GetAccessTokenForRequestAsync();
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    public async Task<JsonElement> PostAsync(string path, object body)
    {
        using var req = await Authed(HttpMethod.Post, $"{DataBase}{path}");
        req.Content = JsonContent.Create(body);
        return await Send(req);
    }

    public async Task<JsonElement> GetAsync(string path)
    {
        using var req = await Authed(HttpMethod.Get, $"{DataBase}{path}");
        return await Send(req);
    }

    private async Task<JsonElement> Send(HttpRequestMessage req)
    {
        var resp = await _httpClient.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"GA4 API error {(int)resp.StatusCode}: {(text.Length > 500 ? text[..500] + "…" : text)}");
        return string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<JsonElement>(text);
    }

    /// <summary>Turns a comma-separated list into GA4 [{ "name": x }] entries. Public for unit testing.</summary>
    public static object[] NameList(string csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(n => (object)new { name = n }).ToArray();

    public void Dispose() => _httpClient.Dispose();
}
