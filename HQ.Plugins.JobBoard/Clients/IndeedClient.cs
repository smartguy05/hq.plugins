using System.Net.Http.Json;
using System.Text.Json;
using HQ.Plugins.JobBoard.Models;

namespace HQ.Plugins.JobBoard.Clients;

internal class IndeedClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IndeedClient(string apiKey, string apiHost)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"https://{apiHost}")
        };

        _httpClient.DefaultRequestHeaders.Add("X-RapidAPI-Key", apiKey);
        _httpClient.DefaultRequestHeaders.Add("X-RapidAPI-Host", apiHost);
    }

    public async Task<List<JobListing>> SearchAsync(string query, string location, int maxResults, string jobType = null)
    {
        var queryParams = new List<string>
        {
            $"query={Uri.EscapeDataString(query)}",
            $"num_pages={Math.Max(1, maxResults / 10)}"
        };

        if (!string.IsNullOrWhiteSpace(location))
            queryParams.Add($"location={Uri.EscapeDataString(location)}");
        if (!string.IsNullOrWhiteSpace(jobType))
            queryParams.Add($"employment_type={Uri.EscapeDataString(jobType)}");

        var response = await _httpClient.GetAsync($"/search?{string.Join("&", queryParams)}");

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Indeed API error {(int)response.StatusCode}: {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var listings = new List<JobListing>();

        if (result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var job in data.EnumerateArray())
            {
                listings.Add(new JobListing
                {
                    Id = $"indeed-{GetStr(job, "id") ?? Guid.NewGuid().ToString("N")[..8]}",
                    Title = GetStr(job, "job_title") ?? GetStr(job, "title"),
                    Company = GetStr(job, "company_name") ?? GetStr(job, "company"),
                    Location = GetStr(job, "location"),
                    Description = GetStr(job, "description"),
                    Salary = GetStr(job, "salary"),
                    JobType = GetStr(job, "employment_type"),
                    Url = GetStr(job, "job_url") ?? GetStr(job, "url"),
                    Source = "indeed",
                    PostedDate = GetStr(job, "date_posted") ?? GetStr(job, "posted_at")
                });
            }
        }

        return listings.Take(maxResults).ToList();
    }

    private static string GetStr(JsonElement e, string prop)
    {
        return e.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    }

    public void Dispose() => _httpClient.Dispose();
}
