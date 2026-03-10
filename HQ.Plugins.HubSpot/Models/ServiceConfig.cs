using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.HubSpot.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("HubSpot private app access token. Create at Settings > Integrations > Private Apps.")]
    public string AccessToken { get; set; }

    [Tooltip("HubSpot API base URL. Override only for testing or proxy setups.")]
    public string BaseUrl { get; set; } = "https://api.hubapi.com";
}
