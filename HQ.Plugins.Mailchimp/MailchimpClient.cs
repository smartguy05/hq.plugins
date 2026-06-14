using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HQ.Plugins.Mailchimp;

/// <summary>Thin Mailchimp Marketing API v3 client (Bearer auth), modeled on AsanaClient.</summary>
public class MailchimpClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public MailchimpClient(string apiKey)
    {
        var dc = DataCenterFromKey(apiKey);
        _baseUrl = $"https://{dc}.api.mailchimp.com/3.0";
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Datacenter is the suffix after the final '-' in the API key (e.g. "...-us21" → "us21").</summary>
    public static string DataCenterFromKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("Mailchimp API key is not configured.");
        var dash = apiKey.LastIndexOf('-');
        if (dash < 0 || dash == apiKey.Length - 1)
            throw new InvalidOperationException("Mailchimp API key is missing its datacenter suffix (e.g. '-us21').");
        return apiKey[(dash + 1)..];
    }

    /// <summary>Mailchimp member id = lowercase-hex MD5 of the lowercased email address.</summary>
    public static string SubscriberHash(string email)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes((email ?? "").Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public Task<JsonElement> GetAsync(string path) => SendAsync(HttpMethod.Get, path, null);
    public Task<JsonElement> PostAsync(string path, object body) => SendAsync(HttpMethod.Post, path, body);
    public Task<JsonElement> PutAsync(string path, object body) => SendAsync(HttpMethod.Put, path, body);
    public Task<JsonElement> PatchAsync(string path, object body) => SendAsync(HttpMethod.Patch, path, body);

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object body)
    {
        using var request = new HttpRequestMessage(method, $"{_baseUrl}{path}");
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _httpClient.SendAsync(request);
        await EnsureSuccess(response);
        var content = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(content) ? default : JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 500) body = body[..500] + "…";
        throw new HttpRequestException($"Mailchimp API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    public void Dispose() => _httpClient.Dispose();
}
