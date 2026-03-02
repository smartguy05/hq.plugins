using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HQ.Plugins.JobBoard.Models;

namespace HQ.Plugins.JobBoard.Clients;

internal class LinkedInJobsClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public LinkedInJobsClient(string proxycurlApiKey)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://nubela.co/proxycurl/api")
        };

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", proxycurlApiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<JobListing>> SearchAsync(string query, string location, int maxResults, string jobType = null)
    {
        var queryParams = new List<string>
        {
            $"keyword={Uri.EscapeDataString(query)}",
            $"page_size={maxResults}"
        };

        if (!string.IsNullOrWhiteSpace(location))
            queryParams.Add($"geo_id={Uri.EscapeDataString(location)}");
        if (!string.IsNullOrWhiteSpace(jobType))
            queryParams.Add($"job_type={Uri.EscapeDataString(jobType)}");

        var response = await _httpClient.GetAsync($"/v2/linkedin/company/job?search_id=&{string.Join("&", queryParams)}");

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"LinkedIn Jobs API error {(int)response.StatusCode}: {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var listings = new List<JobListing>();

        if (result.TryGetProperty("job", out var jobs) && jobs.ValueKind == JsonValueKind.Array)
        {
            foreach (var job in jobs.EnumerateArray())
            {
                listings.Add(new JobListing
                {
                    Id = $"linkedin-{GetStr(job, "job_url")?.GetHashCode().ToString("X8") ?? Guid.NewGuid().ToString("N")[..8]}",
                    Title = GetStr(job, "job_title"),
                    Company = GetStr(job, "company"),
                    Location = GetStr(job, "location"),
                    Description = GetStr(job, "job_description"),
                    Url = GetStr(job, "job_url"),
                    Source = "linkedin",
                    PostedDate = GetStr(job, "listed_at")
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
