using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace HQ.Plugins.Jira;

internal class JiraClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public JiraClient(string domain, string email, string apiToken)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"https://{domain}.atlassian.net")
        };

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{email}:{apiToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // Standard API helpers

    public async Task<JsonElement> GetAsync(string path)
    {
        var response = await _httpClient.GetAsync($"/rest/api/3/{path}");
        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> PostAsync(string path, object body)
    {
        var response = await _httpClient.PostAsJsonAsync($"/rest/api/3/{path}", body, JsonOptions);
        await EnsureSuccess(response);
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return default;
        return JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
    }

    public async Task<JsonElement> PutAsync(string path, object body)
    {
        var response = await _httpClient.PutAsJsonAsync($"/rest/api/3/{path}", body, JsonOptions);
        await EnsureSuccess(response);
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return default;
        return JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
    }

    public async Task DeleteAsync(string path)
    {
        var response = await _httpClient.DeleteAsync($"/rest/api/3/{path}");
        await EnsureSuccess(response);
    }

    // Agile API helpers

    public async Task<JsonElement> GetAgileAsync(string path)
    {
        var response = await _httpClient.GetAsync($"/rest/agile/1.0/{path}");
        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }

    public async Task<JsonElement> PostAgileAsync(string path, object body)
    {
        var response = await _httpClient.PostAsJsonAsync($"/rest/agile/1.0/{path}", body, JsonOptions);
        await EnsureSuccess(response);
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
            return default;
        return JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
    }

    // ADF helpers

    public static object ToAdf(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new { type = "doc", version = 1, content = Array.Empty<object>() };

        var paragraphs = text.Split('\n');
        var content = new List<object>();

        foreach (var paragraph in paragraphs)
        {
            if (string.IsNullOrEmpty(paragraph))
            {
                content.Add(new { type = "paragraph", content = Array.Empty<object>() });
            }
            else
            {
                content.Add(new
                {
                    type = "paragraph",
                    content = new object[]
                    {
                        new { type = "text", text = paragraph }
                    }
                });
            }
        }

        return new { type = "doc", version = 1, content };
    }

    public static string FromAdf(JsonElement adf)
    {
        if (adf.ValueKind == JsonValueKind.Undefined || adf.ValueKind == JsonValueKind.Null)
            return string.Empty;

        var sb = new StringBuilder();
        ExtractText(adf, sb);
        return sb.ToString().TrimEnd();
    }

    private static void ExtractText(JsonElement element, StringBuilder sb)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (element.TryGetProperty("type", out var typeProp))
        {
            var type = typeProp.GetString();

            if (type == "text" && element.TryGetProperty("text", out var textProp))
            {
                sb.Append(textProp.GetString());
                return;
            }

            if (type == "hardBreak")
            {
                sb.AppendLine();
                return;
            }
        }

        if (element.TryGetProperty("content", out var contentProp) && contentProp.ValueKind == JsonValueKind.Array)
        {
            var isDoc = element.TryGetProperty("type", out var docType) && docType.GetString() == "doc";
            var isParagraph = element.TryGetProperty("type", out var paraType) && paraType.GetString() == "paragraph";

            foreach (var child in contentProp.EnumerateArray())
            {
                ExtractText(child, sb);
            }

            if (isParagraph || (isDoc && false))
            {
                sb.AppendLine();
            }
        }
    }

    // Error handling

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"Jira API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
