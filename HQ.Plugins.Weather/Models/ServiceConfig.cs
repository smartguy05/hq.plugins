using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Weather.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Sensitive]
    [Tooltip("OpenWeatherMap API key (requires a One Call API 3.0 subscription)")]
    public string ApiKey { get; set; }

    [Tooltip("Default units when a request does not specify one: metric | imperial | standard (default metric)")]
    public string DefaultUnits { get; set; }
}
