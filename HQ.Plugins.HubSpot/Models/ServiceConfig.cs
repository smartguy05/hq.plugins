using HQ.Models.Interfaces;

namespace HQ.Plugins.HubSpot.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string AccessToken { get; set; }
    public string BaseUrl { get; set; } = "https://api.hubapi.com";
}
