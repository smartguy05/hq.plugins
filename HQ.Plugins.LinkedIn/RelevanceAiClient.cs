using System.Text;
using System.Text.Json;

namespace HQ.Plugins.LinkedIn;

public class RelevanceAiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _projectId;
    private readonly bool _ownsHttpClient;

    public RelevanceAiClient(string apiKey, string region, string projectId, HttpClient httpClient = null)
    {
        _projectId = projectId;

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }

        _httpClient.BaseAddress = new Uri($"https://api-{region}.stack.tryrelevance.com/latest/");
        _httpClient.DefaultRequestHeaders.Remove("Authorization");
        _httpClient.DefaultRequestHeaders.Add("Authorization", apiKey);
    }

    public async Task<JsonElement> TriggerTool(string toolId, Dictionary<string, object> parameters)
    {
        var payload = new
        {
            @params = parameters,
            project = _projectId
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"studios/{toolId}/trigger_limited", content);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(responseBody);
    }

    public async Task<JsonElement> ListTools()
    {
        var response = await _httpClient.GetAsync($"studios/list?project_id={_projectId}");
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(responseBody);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
