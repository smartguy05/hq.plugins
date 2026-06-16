using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Maps.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Sensitive]
    [Tooltip("Google Maps Platform API key (enable Directions, Distance Matrix, Places and Geocoding APIs)")]
    public string ApiKey { get; set; }
}
