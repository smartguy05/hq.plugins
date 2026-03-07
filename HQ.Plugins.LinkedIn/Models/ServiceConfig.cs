using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.LinkedIn.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("LinkedIn OAuth access token for posting and profile access")]
    public string LinkedInAccessToken { get; set; }

    [Tooltip("Your LinkedIn person URN, e.g. urn:li:person:AbCdEf123")]
    public string LinkedInPersonUrn { get; set; }

    [Tooltip("Proxycurl API key for LinkedIn data enrichment. Get one at https://nubela.co/proxycurl")]
    public string ProxycurlApiKey { get; set; }

    [Tooltip("Proxycurl API base URL. Override only for testing or proxy setups.")]
    public string ProxycurlBaseUrl { get; set; } = "https://nubela.co/proxycurl/api";
}
