using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.JobBoard.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("RapidAPI key for Indeed job search")]
    public string IndeedApiKey { get; set; }

    [Tooltip("RapidAPI host for Indeed, e.g. indeed12.p.rapidapi.com")]
    public string IndeedApiHost { get; set; }

    [Tooltip("Upwork RSS feed URL for job listings. Build at https://www.upwork.com/nx/search/jobs/ and copy the RSS link.")]
    public string UpworkRssFeedUrl { get; set; }

    [Tooltip("Proxycurl API key for LinkedIn job search enrichment")]
    public string ProxycurlApiKey { get; set; }

    [Tooltip("Enable Toptal job scraping. Requires a compatible scraper setup.")]
    public bool EnableToptal { get; set; } = false;

    [Tooltip("Directory for storing application tracking data, e.g. /data/job-applications")]
    public string DataDirectory { get; set; }
}
