using System.Xml;
using HQ.Plugins.JobBoard.Models;

namespace HQ.Plugins.JobBoard.Clients;

internal class UpworkClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseFeedUrl;

    public UpworkClient(string rssFeedUrl)
    {
        _baseFeedUrl = rssFeedUrl;
        _httpClient = new HttpClient();
    }

    public async Task<List<JobListing>> SearchAsync(string query, int maxResults, string skills = null)
    {
        var feedUrl = _baseFeedUrl;

        // Upwork RSS feeds support query parameters
        if (!string.IsNullOrWhiteSpace(query))
        {
            var separator = feedUrl.Contains('?') ? "&" : "?";
            feedUrl += $"{separator}q={Uri.EscapeDataString(query)}";
        }

        var response = await _httpClient.GetAsync(feedUrl);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Upwork RSS error {(int)response.StatusCode}: {error}");
        }

        var xmlContent = await response.Content.ReadAsStringAsync();
        var listings = ParseRssFeed(xmlContent);

        if (!string.IsNullOrWhiteSpace(skills))
        {
            var skillList = skills.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            listings = listings.Where(l =>
                skillList.Any(s =>
                    (l.Title?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (l.Description?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false)
                )).ToList();
        }

        return listings.Take(maxResults).ToList();
    }

    private static List<JobListing> ParseRssFeed(string xml)
    {
        var listings = new List<JobListing>();
        var doc = new XmlDocument();
        doc.LoadXml(xml);

        var items = doc.SelectNodes("//item");
        if (items == null) return listings;

        foreach (XmlNode item in items)
        {
            var title = item.SelectSingleNode("title")?.InnerText;
            var link = item.SelectSingleNode("link")?.InnerText;
            var description = item.SelectSingleNode("description")?.InnerText;
            var pubDate = item.SelectSingleNode("pubDate")?.InnerText;

            listings.Add(new JobListing
            {
                Id = $"upwork-{Guid.NewGuid().ToString("N")[..8]}",
                Title = title,
                Company = "via Upwork",
                Description = description,
                Url = link,
                Source = "upwork",
                PostedDate = pubDate,
                JobType = "freelance"
            });
        }

        return listings;
    }

    public void Dispose() => _httpClient.Dispose();
}
