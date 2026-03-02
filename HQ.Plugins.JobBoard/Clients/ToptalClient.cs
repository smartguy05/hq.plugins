using HQ.Plugins.JobBoard.Models;
using HtmlAgilityPack;

namespace HQ.Plugins.JobBoard.Clients;

internal class ToptalClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public ToptalClient()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (compatible; HQ-Bot/1.0)");
    }

    public async Task<List<JobListing>> SearchAsync(string query, int maxResults)
    {
        var url = $"https://www.toptal.com/freelance-jobs?query={Uri.EscapeDataString(query)}";
        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Toptal scrape error {(int)response.StatusCode}: {error}");
        }

        var html = await response.Content.ReadAsStringAsync();
        return ParseHtml(html, maxResults);
    }

    private static List<JobListing> ParseHtml(string html, int maxResults)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var listings = new List<JobListing>();
        var jobNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'job-listing')]")
            ?? doc.DocumentNode.SelectNodes("//article[contains(@class, 'job')]");

        if (jobNodes == null)
            return listings;

        foreach (var node in jobNodes.Take(maxResults))
        {
            var titleNode = node.SelectSingleNode(".//h3|.//h2|.//a[contains(@class, 'title')]");
            var linkNode = node.SelectSingleNode(".//a[@href]");
            var descNode = node.SelectSingleNode(".//p|.//div[contains(@class, 'description')]");

            var href = linkNode?.GetAttributeValue("href", "");
            if (!string.IsNullOrEmpty(href) && !href.StartsWith("http"))
                href = $"https://www.toptal.com{href}";

            listings.Add(new JobListing
            {
                Id = $"toptal-{Guid.NewGuid().ToString("N")[..8]}",
                Title = titleNode?.InnerText?.Trim(),
                Company = "via Toptal",
                Description = descNode?.InnerText?.Trim(),
                Url = href,
                Source = "toptal",
                JobType = "freelance"
            });
        }

        return listings;
    }

    public void Dispose() => _httpClient.Dispose();
}
