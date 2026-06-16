using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Zendesk.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Zendesk subdomain — the '{subdomain}' in https://{subdomain}.zendesk.com")]
    public string Subdomain { get; set; }

    [Tooltip("Agent email address used for API token auth")]
    public string Email { get; set; }

    [Sensitive]
    [Tooltip("Zendesk API token (Admin Center → Apps and integrations → APIs → Zendesk API)")]
    public string ApiToken { get; set; }
}
