using HQ.Models.Interfaces;

namespace HQ.Plugins.JobBoard.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    // Indeed (via RapidAPI or similar aggregator)
    public string IndeedApiKey { get; set; }
    public string IndeedApiHost { get; set; }

    // Upwork RSS
    public string UpworkRssFeedUrl { get; set; }

    // LinkedIn Jobs (via Proxycurl)
    public string ProxycurlApiKey { get; set; }

    // Toptal (scraping)
    public bool EnableToptal { get; set; } = false;

    // Application tracking
    public string DataDirectory { get; set; }
}
