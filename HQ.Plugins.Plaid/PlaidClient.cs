using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HQ.Plugins.Plaid;

/// <summary>Thin Plaid client. Plaid authenticates by including client_id/secret in every JSON body.</summary>
internal class PlaidClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _clientId;
    private readonly string _secret;

    public PlaidClient(string baseUrl, string clientId, string secret)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _clientId = clientId;
        _secret = secret;
        _httpClient = new HttpClient();
    }

    /// <summary>POST a Plaid endpoint, injecting client_id/secret into the body.</summary>
    public async Task<JsonElement> PostAsync(string path, JsonObject body)
    {
        body["client_id"] = _clientId;
        body["secret"] = _secret;
        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}{path}", body);
        await EnsureSuccess(response);
        var content = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(content) ? default : JsonSerializer.Deserialize<JsonElement>(content);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 600) body = body[..600] + "…";
        throw new HttpRequestException($"Plaid API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    public void Dispose() => _httpClient.Dispose();
}
