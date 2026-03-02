using HQ.Models.Interfaces;

namespace HQ.Plugins.LinkedIn.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    // LinkedIn OAuth
    public string LinkedInAccessToken { get; set; }
    public string LinkedInPersonUrn { get; set; }

    // Proxycurl
    public string ProxycurlApiKey { get; set; }
    public string ProxycurlBaseUrl { get; set; } = "https://nubela.co/proxycurl/api";
}
