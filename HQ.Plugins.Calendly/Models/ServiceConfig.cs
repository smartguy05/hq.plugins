using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Calendly.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Sensitive]
    [Tooltip("Calendly personal access token (Integrations → API & Webhooks)")]
    public string AccessToken { get; set; }
}
