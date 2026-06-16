using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Health.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Terra developer id (dev-id header)")]
    public string DevId { get; set; }

    [Sensitive]
    [Tooltip("Terra API key (x-api-key header)")]
    public string ApiKey { get; set; }
}
